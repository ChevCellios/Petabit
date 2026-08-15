using System.Diagnostics;

namespace Petabit;

public sealed class RequestObservabilityMiddleware(
    RequestDelegate next,
    ILogger<RequestObservabilityMiddleware> logger)
{
    private const string CorrelationHeader = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);
        context.TraceIdentifier = correlationId;
        context.Response.Headers[CorrelationHeader] = correlationId;

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId
        });

        var startedAt = Stopwatch.GetTimestamp();
        await next(context);

        var elapsedMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            logger.LogDebug(
                "Health request completed with status {StatusCode} in {ElapsedMilliseconds:F1} ms.",
                context.Response.StatusCode,
                elapsedMilliseconds);
            return;
        }

        logger.LogInformation(
            "Request completed with status {StatusCode} in {ElapsedMilliseconds:F1} ms.",
            context.Response.StatusCode,
            elapsedMilliseconds);
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        var suppliedValue = context.Request.Headers[CorrelationHeader].FirstOrDefault();
        if (Guid.TryParse(suppliedValue, out var suppliedId))
        {
            return suppliedId.ToString("N");
        }

        if (Guid.TryParse(context.TraceIdentifier, out var existingId))
        {
            return existingId.ToString("N");
        }

        return Guid.NewGuid().ToString("N");
    }
}
