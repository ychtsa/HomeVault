using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace HomeVault.Middleware;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken cancellationToken)
    {
        var originalPath = context.Features.Get<IExceptionHandlerPathFeature>()?.Path
            ?? context.Request.Path.Value ?? "";
        if (!new PathString(originalPath).StartsWithSegments("/api")) return false;
        logger.LogError(exception, "API request failed: {Method} {Path}",
            context.Request.Method, originalPath);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.Headers.CacheControl = "no-store";
        SecurityHeadersMiddleware.ApplyHeaders(context.Response.Headers);
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred. Please try again.",
            Extensions = { ["traceId"] = context.TraceIdentifier }
        }, options: (System.Text.Json.JsonSerializerOptions?)null,
            contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}
