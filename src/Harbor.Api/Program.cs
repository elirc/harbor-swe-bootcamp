using Harbor.Api.Infrastructure;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options =>
    {
        // Every controller endpoint requires an authenticated teammate unless
        // marked [AllowAnonymous]; unknown routes still surface as plain 404s.
        options.Filters.Add(new AuthorizeFilter());
        options.Filters.Add<WorkspaceScopeFilter>();
    })
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddOpenApi();

builder.Services.AddProblemDetails();
// Order matters: the first handler that recognizes the exception wins.
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<ConcurrencyExceptionHandler>();
builder.Services.AddExceptionHandler<WriteBusyExceptionHandler>();

builder.Services.AddDbContext<HarborDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("Harbor") ?? "Data Source=harbor.db"));

// Webhook delivery. The sender is an interface so tests can dispatch without a
// live endpoint; the timeout keeps one slow subscriber from stalling a drain.
builder.Services.AddHttpClient<IWebhookSender, HttpWebhookSender>(client =>
    client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddScoped<WebhookDispatcher>();
builder.Services.AddScoped<ConversationQueries>();
builder.Services.AddScoped<SnoozeWake>();
builder.Services.AddScoped<PublicationScheduler>();
builder.Services.AddHostedService<PublicationScheduleWorker>();
builder.Services.AddSingleton(TimeProvider.System);
var privateRoot = Path.GetFullPath(builder.Configuration["Storage:PrivateRoot"]
    ?? Path.Combine(builder.Environment.ContentRootPath, ".harbor-private"));
var publicRoot = Path.GetFullPath(builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"));
if (privateRoot.Equals(publicRoot, StringComparison.OrdinalIgnoreCase)
    || privateRoot.StartsWith(publicRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Private storage must be outside the web root.");
builder.Services.AddSingleton(_ => new PrivateFiles(privateRoot));
builder.Services.AddScoped<AttachmentReconciliation>();
builder.Services.AddDataProtection().SetApplicationName("Harbor")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(privateRoot, "protection-keys")));
builder.Services.AddSingleton<EventCursors>();
builder.Services.AddScoped<ConversationExports>();
builder.Services.AddHostedService<ConversationExportWorker>();

builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, null);

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

// After authentication, so each line can name the teammate who made the call.
app.UseMiddleware<RequestLoggingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<HarborDbContext>();
    db.Database.Migrate();
    DataSeeder.Seed(db);
}

app.MapControllers();

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;
