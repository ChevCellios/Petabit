using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Petabit;

public sealed class IssApiHealthCheck(IHttpClientFactory httpClientFactory) : IHealthCheck
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private HealthCheckResult? cached;
    private DateTimeOffset checkedAt;
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cached.HasValue && DateTimeOffset.UtcNow - checkedAt < TimeSpan.FromSeconds(15)) return cached.Value;
            cached = await ProbeAsync();
            checkedAt = DateTimeOffset.UtcNow;
            return cached.Value;
        }
        finally { gate.Release(); }
    }

    private async Task<HealthCheckResult> ProbeAsync()
    {
        try
        {
            using var response = await httpClientFactory
                .CreateClient("iss-health")
                .GetAsync("satellites/25544", HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("ISS API is available.")
                : HealthCheckResult.Unhealthy($"ISS API returned HTTP {(int)response.StatusCode}.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("ISS API is unavailable.", exception);
        }
    }
}
