using Harbor.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Microsoft.AspNetCore.DataProtection;

namespace Harbor.Tests.Integration;

/// <summary>
/// Hosts the API against a shared in-memory SQLite database. The connection
/// stays open for the factory's lifetime so the schema survives between
/// requests.
/// </summary>
public class HarborApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    /// <summary>Optional isolated file for real multi-connection HTTP races; the test owns cleanup.</summary>
    public string? FileDatabasePath { get; init; }
    public string PrivateStorageRoot { get; init; } = Path.Combine(Path.GetTempPath(), "harbor-test-storage-" + Guid.NewGuid().ToString("N"));
    /// <summary>Optional SQL failure injection for transaction rollback rehearsals.</summary>
    public Func<DbCommand, bool>? FailCommand { get; set; }
    private sealed class FailureInterceptor(HarborApiFactory factory) : DbCommandInterceptor
    {
        private void Check(DbCommand command)
        { if (factory.FailCommand?.Invoke(command) == true) throw new InvalidOperationException("Injected database-command failure"); }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Check(command); return base.ReaderExecutingAsync(command, eventData, result, cancellationToken); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Check(command); return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken); }
    }

    /// <summary>Captures webhook attempts in place of real HTTP delivery.</summary>
    public FakeWebhookSender WebhookSender { get; } = new();

    /// <summary>Only paths using the injectable clock observe this; null uses real UTC.</summary>
    public DateTimeOffset? FrozenTime { get; set; }
    private sealed class FactoryClock(HarborApiFactory factory) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => factory.FrozenTime ?? DateTimeOffset.UtcNow;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<HarborDbContext>));
            services.RemoveAll(typeof(HarborDbContext));

            if (FileDatabasePath is null)
            {
                _connection.Open();
                services.AddDbContext<HarborDbContext>(options => options.UseSqlite(_connection).AddInterceptors(new FailureInterceptor(this)));
            }
            else services.AddDbContext<HarborDbContext>(options =>
                options.UseSqlite($"Data Source={FileDatabasePath};Pooling=False;Default Timeout=1").AddInterceptors(new FailureInterceptor(this)));

            services.RemoveAll(typeof(IWebhookSender));
            services.RemoveAll<PrivateFiles>();
            services.AddSingleton(new PrivateFiles(PrivateStorageRoot));
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(PrivateStorageRoot, "protection-keys")));
            services.AddSingleton<IWebhookSender>(WebhookSender);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FactoryClock(this));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HarborDbContext>();
        if (FileDatabasePath is null) db.Database.EnsureCreated();
        else db.Database.Migrate();

        return host;
    }

    /// <summary>Runs an action against a fresh DbContext scope (arrange/assert helper).</summary>
    public void WithDb(Action<HarborDbContext> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HarborDbContext>();
        action(db);
    }

    public void SeedDemoData() => WithDb(DataSeeder.Seed);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
            if (Directory.Exists(PrivateStorageRoot)) Directory.Delete(PrivateStorageRoot, recursive: true);
        }
    }
}
