using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Petabit;

public sealed class IssApiHealthCheck(IHttpClientFactory httpClientFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClientFactory
                .CreateClient("iss-health")
                .GetAsync("satellites/25544", HttpCompletionOption.ResponseHeadersRead, cancellationToken);

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
