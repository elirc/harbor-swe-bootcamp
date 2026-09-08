using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Infrastructure;

public sealed class WriteBusyException : Exception
{
    public WriteBusyException(Exception inner) : base("The database writer is busy. Retry this command.", inner) { }
}

/// <summary>Reserve SQLite's writer before decision reads. Callbacks must contain database work only.</summary>
public static class WriteTransactions
{
    public static async Task<T> RunAsync<T>(HarborDbContext db, Func<Task<T>> work, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is not null) return await work();
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        var previousTimeout = connection.DefaultTimeout;
        connection.DefaultTimeout = 1;
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await using var transaction = connection.BeginTransaction(deferred: false);
                    await using var enlisted = await db.Database.UseTransactionAsync(transaction, ct);
                    var result = await work();
                    await transaction.CommitAsync(ct);
                    return result;
                }
                catch (Exception ex) when (Busy(ex))
                {
                    // Retry with fresh tracked state; never replay a callback containing HTTP or file effects.
                    db.ChangeTracker.Clear();
                    if (attempt == 2) throw new WriteBusyException(ex);
                    await Task.Delay(TimeSpan.FromMilliseconds(25 * (attempt + 1)), ct);
                }
            }
        }
        finally
        {
            connection.DefaultTimeout = previousTimeout;
            await db.Database.CloseConnectionAsync();
        }
    }

    private static bool Busy(Exception ex) => ex is SqliteException { SqliteErrorCode: 5 or 6 }
        || ex is DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 5 or 6 } };
}
