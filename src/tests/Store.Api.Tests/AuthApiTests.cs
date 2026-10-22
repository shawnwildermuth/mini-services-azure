using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Store.Data;

namespace Store.Api.Tests;

public class AuthApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  [Fact]
  public async Task ProtectedEndpoints_RequireAuthentication()
  {
    var client = factory.CreateClient();

    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/cart")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/addresses")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
  }

  [Fact]
  public async Task RegisterAndLogin_GrantsAccessToProtectedEndpoints()
  {
    var user = await factory.CreateUserAsync();

    Assert.Equal(HttpStatusCode.OK, (await user.Http.GetAsync("/api/cart")).StatusCode);
    var info = await user.Http.GetFromJsonAsync<Dictionary<string, object>>("/api/auth/manage/info");
    Assert.Equal(user.Email, info!["email"].ToString());
  }

  [Fact]
  public async Task Register_RejectsWeakPasswordAndDuplicateEmail()
  {
    var client = factory.CreateClient();
    var email = $"dup-{Guid.NewGuid():N}@example.com";

    var weak = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "abc" });
    Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);

    Assert.True((await client.PostAsJsonAsync("/api/auth/register", new { email, password = ApiFactory.Password })).IsSuccessStatusCode);
    var duplicate = await client.PostAsJsonAsync("/api/auth/register", new { email, password = ApiFactory.Password });
    Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
  }

  [Fact]
  public async Task Login_WithWrongPassword_IsUnauthorized()
  {
    var user = await factory.CreateUserAsync();

    var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = "Wrong-password1" });

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task Login_LocksOutAfterRepeatedFailures()
  {
    var user = await factory.CreateUserAsync();
    var client = factory.CreateClient();

    for (var i = 0; i < 5; i++)
      await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = "Wrong-password1" });

    // Even the right password is refused while locked out.
    var response = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = ApiFactory.Password });
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task Refresh_IssuesNewTokens()
  {
    var user = await factory.CreateUserAsync();

    var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = user.Tokens.RefreshToken });

    response.EnsureSuccessStatusCode();
    var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
    Assert.False(string.IsNullOrEmpty(tokens!.AccessToken));
  }

  [Fact]
  public async Task TokenSigningKeys_AreStoredInTheDatabase_SoTheySurviveRestartsAndAreSharedBetweenInstances()
  {
    await factory.CreateUserAsync(); // issuing a bearer token forces a key to be created

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
    Assert.True(await db.DataProtectionKeys.AnyAsync());
  }

  [Fact]
  public async Task Logout_RevokesRefreshToken()
  {
    var user = await factory.CreateUserAsync();

    Assert.Equal(HttpStatusCode.NoContent, (await user.Http.PostAsync("/api/auth/logout", null)).StatusCode);

    var refresh = await factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = user.Tokens.RefreshToken });
    Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
  }

  [Fact]
  public async Task PasswordReset_ChangesPassword()
  {
    var user = await factory.CreateUserAsync();
    var client = factory.CreateClient();
    const string newPassword = "N3w-Passw0rd!";

    // Reset codes are only sent to confirmed addresses.
    Assert.True((await client.PostAsJsonAsync("/api/auth/forgotPassword", new { email = user.Email })).IsSuccessStatusCode);
    Assert.Null(factory.Emails.ResetCodeFor(user.Email));

    using (var scope = factory.Services.CreateScope())
    {
      var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
      var appUser = (await users.FindByEmailAsync(user.Email))!;
      Assert.True((await users.ConfirmEmailAsync(appUser, await users.GenerateEmailConfirmationTokenAsync(appUser))).Succeeded);
    }

    Assert.True((await client.PostAsJsonAsync("/api/auth/forgotPassword", new { email = user.Email })).IsSuccessStatusCode);
    var code = factory.Emails.ResetCodeFor(user.Email);
    Assert.NotNull(code);

    var reset = await client.PostAsJsonAsync("/api/auth/resetPassword", new { email = user.Email, resetCode = code, newPassword });
    Assert.True(reset.IsSuccessStatusCode);

    Assert.Equal(HttpStatusCode.Unauthorized,
      (await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = ApiFactory.Password })).StatusCode);
    Assert.True((await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = newPassword })).IsSuccessStatusCode);
  }

  [Fact]
  public async Task ForgotPassword_DoesNotRevealWhetherAccountExists()
  {
    var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/forgotPassword", new { email = "nobody@example.com" });

    Assert.True(response.IsSuccessStatusCode);
  }

  [Fact]
  public async Task AuthEndpoints_AreRateLimited()
  {
    using var limited = factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
      services.Configure<RateLimitSettings>(o => o.AuthPerMinute = 3)));
    var client = limited.CreateClient();

    var statuses = new List<HttpStatusCode>();
    for (var i = 0; i < 5; i++)
      statuses.Add((await client.PostAsJsonAsync("/api/auth/login", new { email = "a@example.com", password = "x" })).StatusCode);

    Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.Unauthorized));
    Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
  }

  [Fact]
  public async Task TokenRefresh_IsNotCaughtByTheStrictCredentialLimit()
  {
    // Page loads refresh the session; many users behind one IP must not be locked out by the login limit.
    using var limited = factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
      services.Configure<RateLimitSettings>(o => o.AuthPerMinute = 2)));
    var client = limited.CreateClient();

    var statuses = new List<HttpStatusCode>();
    for (var i = 0; i < 6; i++)
      statuses.Add((await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "stale" })).StatusCode);

    Assert.All(statuses, s => Assert.Equal(HttpStatusCode.Unauthorized, s));
  }

  [Fact]
  public async Task Cors_AllowsConfiguredOriginsOnly()
  {
    async Task<string?> AllowOrigin(string origin)
    {
      var request = new HttpRequestMessage(HttpMethod.Options, "/api/products");
      request.Headers.Add("Origin", origin);
      request.Headers.Add("Access-Control-Request-Method", "GET");
      var response = await factory.CreateClient().SendAsync(request);
      return response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? values.Single() : null;
    }

    Assert.Equal("http://localhost:5173", await AllowOrigin("http://localhost:5173"));
    Assert.Null(await AllowOrigin("https://evil.example"));
  }
}
