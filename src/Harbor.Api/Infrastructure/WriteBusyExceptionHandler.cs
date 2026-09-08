using Harbor.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Harbor.Api.Infrastructure;

public sealed class WriteBusyExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not WriteBusyException) return false;
        context.Response.StatusCode = 503;
        context.Response.Headers.RetryAfter = "1";
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = 503, Title = "Writer temporarily busy", Detail = "Retry this command after one second." } });
    }
}
