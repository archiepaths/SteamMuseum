using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SteamMuseum.Application;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch {
            BusinessException e => (e.Status, e.Message),
            DbUpdateConcurrencyException => (409, "The record changed. Reload and try again."),
            DbUpdateException => (409, "The change conflicts with existing records."),
            _ => (500, "An unexpected error occurred.")
        };
        if (status == 500) logger.LogError(exception, "Unhandled API error {TraceId}", context.TraceIdentifier);
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ProblemDetails {
            Status = status, Title = title, Extensions = { ["traceId"] = context.TraceIdentifier }
        }, ct);
        return true;
    }
}
