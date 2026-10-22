using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Store.Api.Tests;

/// <summary>Security regression tests: they encode decisions from the security review (src/docs/security-review.md).</summary>
public class SecurityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  [Fact]
  public async Task CookieSignIn_IsRefused_BecauseTheApiIsBearerOnly()
  {
    var user = await factory.CreateUserAsync();

    foreach (var query in new[] { "useCookies=true", "useSessionCookies=true", "useCookies=true&useSessionCookies=true" })
    {
      var response = await factory.CreateClient().PostAsJsonAsync($"/api/auth/login?{query}", new { email = user.Email, password = ApiFactory.Password });

      Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
      Assert.False(response.Headers.Contains("Set-Cookie"), $"?{query} must not set a cookie");
    }

    // Explicitly turning cookies off is the same as the default, so it is allowed.
    var plain = await factory.CreateClient().PostAsJsonAsync("/api/auth/login?useCookies=false", new { email = user.Email, password = ApiFactory.Password });
    Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
    Assert.False(plain.Headers.Contains("Set-Cookie"));
  }

  [Fact]
  public async Task Responses_CarryDefensiveHeaders()
  {
    var response = await factory.CreateClient().GetAsync("/api/categories");

    Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
  }

  [Theory]
  [InlineData("/api/cart")]
  [InlineData("/api/orders")]
  [InlineData("/api/addresses")]
  [InlineData("/api/auth/manage/info")]
  public async Task PersonalData_IsNeverCachedByBrowsersOrProxies(string path)
  {
    var user = await factory.CreateUserAsync();

    var response = await user.Http.GetAsync(path);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
  }

  [Fact]
  public async Task AdminResponses_AreNeverCached()
  {
    var response = await factory.CreateAdminClient().GetAsync("/api/admin/dashboard");

    Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
  }

  [Fact]
  public async Task Errors_DoNotLeakInternalDetails()
  {
    // A malformed body on a validated endpoint: the response is a problem document, not a stack trace.
    var user = await factory.CreateUserAsync();
    var response = await user.Http.PostAsync("/api/addresses", new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"));
    var body = await response.Content.ReadAsStringAsync();

    Assert.True((int)response.StatusCode is >= 400 and < 500, $"status was {(int)response.StatusCode}: {body}");
    Assert.DoesNotContain("   at ", body);       // stack frames
    Assert.DoesNotContain("Exception", body);
    Assert.DoesNotContain(".cs:line", body);
  }

  /// <summary>
  /// The complete list of endpoints that anyone may call without signing in. Adding an endpoint without authorization
  /// fails this test until it is added here on purpose, which is the point.
  /// </summary>
  private static readonly string[] AnonymousEndpoints =
  [
    // public catalog
    "/api/categories",
    "/api/products",
    "/api/products/facets",
    "/api/products/{sku}",
    "/api/products/{sku}/availability",
    // Identity: credentials are what these endpoints check (rate limited per IP)
    "/api/auth/register",
    "/api/auth/login",
    "/api/auth/refresh",
    "/api/auth/confirmEmail",
    "/api/auth/resendConfirmationEmail",
    "/api/auth/forgotPassword",
    "/api/auth/resetPassword",
    // probes
    "/health/live",
    "/health/ready",
    // development-only; not mapped outside Development (tested separately)
    "/api/dev/admin-token",
    "/openapi/{documentName}.json",
  ];

  [Fact]
  public void EveryEndpointRequiresSignIn_ExceptTheOnesListedAsPublic()
  {
    var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
    Assert.True(endpoints.Count > 40, "expected to find the application's endpoints");

    var anonymous = endpoints
      .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null || e.Metadata.GetMetadata<IAuthorizeData>() is null)
      .Select(e => e.RoutePattern.RawText!)
      .Distinct()
      .Order()
      .ToList();

    Assert.Equal(AnonymousEndpoints.Order(), anonymous);
  }

  [Fact]
  public void EveryAdminEndpoint_DemandsTheAdminPolicy_NotJustAnyLogin()
  {
    var admin = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
      .Where(e => e.RoutePattern.RawText!.StartsWith("/api/admin/", StringComparison.Ordinal))
      .ToList();
    Assert.True(admin.Count >= 15, $"expected the admin endpoints, found {admin.Count}");

    foreach (var endpoint in admin)
    {
      var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList();
      Assert.Contains("Admin", policies);
    }
  }

  [Fact]
  public void EveryCustomerEndpoint_UsesCustomerAuthentication_NotAnAdminToken()
  {
    // Customer endpoints carry no scheme restriction, so they use the default (Identity bearer) scheme only:
    // an admin (Entra) token must not be accepted there. Covered end to end by AdminTokens_DoNotWorkOnCustomerEndpoints.
    var customer = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
      .Where(e => e.RoutePattern.RawText!.StartsWith("/api/cart") || e.RoutePattern.RawText.StartsWith("/api/orders") || e.RoutePattern.RawText.StartsWith("/api/addresses"));

    foreach (var endpoint in customer)
      Assert.DoesNotContain(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(), a => a.AuthenticationSchemes?.Contains("Entra") == true);
  }
}
