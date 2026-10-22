using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Store.Api;

public class RateLimitSettings
{
  public int GlobalPerMinute { get; set; } = 300;
  public int AuthPerMinute { get; set; } = 20;
}

public static class RateLimiting
{
  public const string AuthPolicy = "auth";

  /// <summary>Per-IP fixed-window limits: a general one for every endpoint and a stricter one for /api/auth.</summary>
  public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
  {
    services.Configure<RateLimitSettings>(configuration.GetSection("RateLimiting"));
    services.AddRateLimiter(_ => { });

    // Configured through IOptions so limits are read after the host is built (and are overridable in tests).
    services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitSettings>>((options, settings) =>
    {
      options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
      options.OnRejected = (context, _) =>
      {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        return ValueTask.CompletedTask;
      };

      options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => PerMinute(settings.Value.GlobalPerMinute)));

      // Strict only where passwords or emails can be guessed or spammed. Token refresh and profile reads happen on
      // every page load, so they only fall under the general limit (a shared office IP must not lock users out).
      options.AddPolicy(AuthPolicy, context => IsCredentialEndpoint(context.Request.Path)
        ? RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => PerMinute(settings.Value.AuthPerMinute))
        : RateLimitPartition.GetNoLimiter("auth-exempt"));
    });

    return services;
  }

  private static readonly string[] CredentialEndpoints =
    ["/login", "/register", "/forgotPassword", "/resetPassword", "/resendConfirmationEmail"];

  private static bool IsCredentialEndpoint(PathString path) =>
    CredentialEndpoints.Any(e => path.Value?.EndsWith(e, StringComparison.OrdinalIgnoreCase) == true);

  private static string ClientKey(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

  private static FixedWindowRateLimiterOptions PerMinute(int permits) => new()
  {
    PermitLimit = permits,
    Window = TimeSpan.FromMinutes(1),
    QueueLimit = 0,
  };
}
