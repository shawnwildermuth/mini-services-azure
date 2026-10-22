using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Store.Api.Health;

/// <summary>Round-trips a key through the distributed cache (Redis in deployed environments).</summary>
public class CacheHealthCheck(IDistributedCache cache) : IHealthCheck
{
  public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
  {
    try
    {
      await cache.SetStringAsync("health:ping", "pong",
        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) }, ct);
      return await cache.GetStringAsync("health:ping", ct) == "pong"
        ? HealthCheckResult.Healthy()
        : HealthCheckResult.Degraded("Cache returned an unexpected value.");
    }
    catch (Exception ex)
    {
      // The catalog cache is an optimization, but reads currently depend on it, so report unhealthy.
      return HealthCheckResult.Unhealthy("Cache unavailable.", ex);
    }
  }
}
