using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Store.Data;

namespace Store.Api.Auth;

public static class AuthEndpoints
{
  public static IServiceCollection AddCustomerAuth(this IServiceCollection services)
  {
    services.AddIdentityApiEndpoints<AppUser>(options =>
      {
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        // TODO: require confirmed email once a real email provider replaces the logging sender.
        options.SignIn.RequireConfirmedEmail = false;
      })
      .AddEntityFrameworkStores<StoreDbContext>();

    // Short-lived access tokens; the refresh token is revoked on logout (see below).
    services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, options =>
    {
      options.BearerTokenExpiration = TimeSpan.FromMinutes(15);
      options.RefreshTokenExpiration = TimeSpan.FromDays(14);
    });

    services.AddSingleton<IEmailSender<AppUser>, LoggingEmailSender>();
    services.AddAuthorization();
    return services;
  }

  /// <summary>
  /// Maps Identity's built-in endpoints under /api/auth: register, login, refresh, forgotPassword,
  /// resetPassword, confirmEmail, resendConfirmationEmail and manage/*. Adds logout.
  /// </summary>
  public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
  {
    var group = app.MapGroup("/api/auth").WithTags("Auth").RequireRateLimiting(RateLimiting.AuthPolicy);
    group.MapIdentityApi<AppUser>();

    // Bearer tokens are stateless, so logout rotates the security stamp: the refresh token stops
    // working immediately and the current access token dies within its 15-minute lifetime.
    group.MapPost("/logout", async (ClaimsPrincipal principal, UserManager<AppUser> users) =>
    {
      var user = await users.GetUserAsync(principal);
      if (user is null) return Results.Unauthorized();

      await users.UpdateSecurityStampAsync(user);
      return Results.NoContent();
    })
    .RequireAuthorization()
    .WithName("Logout")
    .Produces(StatusCodes.Status204NoContent);

    return app;
  }

  /// <summary>The signed-in customer's email (their user name, too).</summary>
  public static string GetEmail(this ClaimsPrincipal principal) =>
    principal.FindFirstValue(ClaimTypes.Email) ?? principal.Identity?.Name ?? "";

  public static string GetUserId(this ClaimsPrincipal principal) =>
    principal.FindFirstValue(ClaimTypes.NameIdentifier)
    ?? throw new InvalidOperationException("Authenticated principal has no user id claim.");
}
