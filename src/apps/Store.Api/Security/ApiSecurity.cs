using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Store.Api.Security;

/// <summary>
/// Cross-cutting HTTP hardening for the API (decisions from the security review, src/docs/security-review.md).
/// </summary>
public static class ApiSecurity
{
  /// <summary>Paths that return one person's data: browsers and shared caches must never store the responses.</summary>
  private static readonly string[] PrivatePaths = ["/api/auth", "/api/cart", "/api/orders", "/api/addresses", "/api/checkout", "/api/admin"];

  public static IServiceCollection AddApiSecurity(this IServiceCollection services)
  {
    services.AddExceptionHandler<ClientErrorExceptionHandler>();
    services.AddHsts(options =>
    {
      options.MaxAge = TimeSpan.FromDays(365);
      options.IncludeSubDomains = true;
    });
    return services;
  }

  public static WebApplication UseApiSecurity(this WebApplication app)
  {
    // HSTS only applies to HTTPS requests; behind App Service they arrive as HTTPS via the forwarded headers.
    if (!app.Environment.IsDevelopment()) app.UseHsts();

    app.Use(async (context, next) =>
    {
      // The SPAs authenticate with bearer tokens only. Identity's login endpoint can also issue an auth COOKIE
      // (?useCookies=true); nothing needs it, and a cookie would add a CSRF-relevant way in, so it is refused.
      if (context.Request.Path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase) && AsksForCookies(context.Request))
      {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
          Status = StatusCodes.Status400BadRequest,
          Title = "Cookie authentication is not supported. Use the returned bearer token.",
        });
        return;
      }

      var isPrivate = PrivatePaths.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));
      context.Response.OnStarting(() =>
      {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        // The API only returns JSON, so no page built from its responses can load anything or be framed.
        // (The Development OpenAPI document is plain JSON too; nothing here renders HTML.)
        headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        if (isPrivate) headers.CacheControl = "no-store";
        return Task.CompletedTask;
      });

      await next();
    });

    return app;
  }

  private static bool AsksForCookies(HttpRequest request) =>
    IsTrue(request.Query["useCookies"]) || IsTrue(request.Query["useSessionCookies"]);

  private static bool IsTrue(Microsoft.Extensions.Primitives.StringValues value) =>
    value.Count > 0 && !value.All(v => string.Equals(v, "false", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A request the framework itself rejects (malformed JSON, body too large, ...) is the client's mistake: answer with its
/// 4xx status instead of letting the generic handler report a 500, which would also page whoever watches error rates.
/// </summary>
public sealed class ClientErrorExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
  public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
  {
    if (exception is not BadHttpRequestException bad) return false;

    httpContext.Response.StatusCode = bad.StatusCode;
    return await problemDetails.TryWriteAsync(new ProblemDetailsContext
    {
      HttpContext = httpContext,
      ProblemDetails = new ProblemDetails
      {
        Status = bad.StatusCode,
        Title = "The request could not be understood.", // never the parser's message: it can echo input back
      },
    });
  }
}
