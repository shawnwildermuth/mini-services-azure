using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Store.Api.Catalog;

public class CatalogCacheOptions
{
  /// <summary>Turn the catalog cache off entirely (every read goes to the database). Useful for load comparisons and debugging.</summary>
  public bool Enabled { get; set; } = true;

  /// <summary>How long an entry may be served before it is rebuilt, even without an admin edit.</summary>
  public int TtlSeconds { get; set; } = 300;
}

/// <summary>
/// Cache-aside helper for catalog data. Every key embeds a version token, so
/// <see cref="InvalidateAsync"/> invalidates all catalog entries at once by changing the token.
/// </summary>
public class CatalogCache(IDistributedCache cache, IOptions<CatalogCacheOptions> options)
{
  private const string VersionKey = "catalog:version";

  private CatalogCacheOptions Options => options.Value;

  public async Task<T?> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T?>> factory, CancellationToken ct)
    where T : class
  {
    if (!Options.Enabled) return await factory(ct);

    var fullKey = $"catalog:{await GetVersionAsync(ct)}:{key}";

    var cached = await cache.GetAsync(fullKey, ct);
    if (cached is not null) return JsonSerializer.Deserialize<T>(cached);

    var value = await factory(ct);
    if (value is not null)
      await cache.SetAsync(fullKey, JsonSerializer.SerializeToUtf8Bytes(value),
        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(Math.Max(Options.TtlSeconds, 1)) }, ct);
    return value;
  }

  public async Task InvalidateAsync(CancellationToken ct = default)
  {
    if (!Options.Enabled) return;
    await cache.SetStringAsync(VersionKey, Guid.NewGuid().ToString("N"), ct);
  }

  private async Task<string> GetVersionAsync(CancellationToken ct)
  {
    var version = await cache.GetStringAsync(VersionKey, ct);
    if (version is not null) return version;

    version = "v0";
    await cache.SetStringAsync(VersionKey, version, ct);
    return version;
  }
}
