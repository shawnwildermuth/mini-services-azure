using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Store.Api.Admin;

public class AzureAdSettings
{
  public string? TenantId { get; set; }
  /// <summary>Application (client) id of the API app registration.</summary>
  public string? ClientId { get; set; }

  public bool IsConfigured => !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId);
}

public class DevTokenSettings
{
  public bool Enabled { get; set; }
}

public static class AdminPolicy
{
  public const string Scheme = "Entra";
  public const string Name = "Admin";
  public const string Role = "Admin";
}

/// <summary>
/// Mints admin tokens signed with a key that exists only in this process's memory. It is registered solely in
/// Development with <c>Admin:DevTokens:Enabled</c>, so the admin site can be run before an Entra tenant exists.
/// </summary>
public class DevTokenIssuer
{
  public const string Issuer = "partsstore-dev";
  public const string Audience = "partsstore-admin-dev";

  public SecurityKey Key { get; } = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));

  public string Issue(string name, params string[] roles) =>
    new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
      Issuer = Issuer,
      Audience = Audience,
      Expires = DateTime.UtcNow.AddHours(8),
      // As in Entra tokens, "roles" is always a JSON array (even with one role).
      Claims = new Dictionary<string, object>
      {
        ["sub"] = "dev-admin",
        ["name"] = name,
        ["roles"] = roles,
      },
      SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256),
    });
}

public static class AdminAuthExtensions
{
  /// <summary>
  /// Admins sign in with Microsoft Entra ID (MSAL in the admin site) and call the API with an access token that
  /// carries the "Admin" app role. This is a separate scheme from the customer Identity bearer tokens, so one
  /// kind of token can never be used for the other's endpoints.
  /// </summary>
  public static IServiceCollection AddAdminAuth(
    this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment, ILogger logger)
  {
    var azureAd = configuration.GetSection("AzureAd").Get<AzureAdSettings>() ?? new();
    var devTokens = environment.IsDevelopment() && (configuration.GetSection("Admin:DevTokens").Get<DevTokenSettings>()?.Enabled ?? false);

    if (devTokens) services.AddSingleton<DevTokenIssuer>();
    if (!azureAd.IsConfigured && !devTokens)
      logger.LogWarning("Admin authentication is not configured (AzureAd:TenantId / AzureAd:ClientId); admin endpoints will reject every request.");

    services.AddAuthentication().AddJwtBearer(AdminPolicy.Scheme, options =>
    {
      options.MapInboundClaims = false; // keep "roles" and "name" as issued
      options.TokenValidationParameters = new TokenValidationParameters
      {
        NameClaimType = "name",
        RoleClaimType = "roles",
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
      };

      if (azureAd.IsConfigured)
      {
        options.Authority = $"https://login.microsoftonline.com/{azureAd.TenantId}/v2.0";
        options.TokenValidationParameters.ValidAudiences = [azureAd.ClientId!, $"api://{azureAd.ClientId}"];
        // v2.0 and v1.0 access tokens name their issuer differently.
        options.TokenValidationParameters.ValidIssuers =
          [$"https://login.microsoftonline.com/{azureAd.TenantId}/v2.0", $"https://sts.windows.net/{azureAd.TenantId}/"];
      }
      else
      {
        options.TokenValidationParameters.ValidAudiences = [];
        options.TokenValidationParameters.ValidIssuers = [];
      }
    });

    // Development-only key, added after the issuer exists in the container.
    services.AddOptions<JwtBearerOptions>(AdminPolicy.Scheme).Configure<IServiceProvider>((options, sp) =>
    {
      var issuer = sp.GetService<DevTokenIssuer>();
      if (issuer is null) return;

      var parameters = options.TokenValidationParameters;
      parameters.ValidIssuers = [..parameters.ValidIssuers ?? [], DevTokenIssuer.Issuer];
      parameters.ValidAudiences = [..parameters.ValidAudiences ?? [], DevTokenIssuer.Audience];
      parameters.IssuerSigningKeys = [..parameters.IssuerSigningKeys ?? [], issuer.Key];
    });

    services.AddAuthorizationBuilder().AddPolicy(AdminPolicy.Name, policy => policy
      .AddAuthenticationSchemes(AdminPolicy.Scheme)
      .RequireAuthenticatedUser()
      .RequireRole(AdminPolicy.Role));

    return services;
  }

  /// <summary>Development helper: POST /api/dev/admin-token returns an admin token (never mapped outside Development).</summary>
  public static IEndpointRouteBuilder MapDevTokenEndpoint(this IEndpointRouteBuilder app)
  {
    if (app.ServiceProvider.GetService<DevTokenIssuer>() is null) return app;

    app.MapPost("/api/dev/admin-token", (DevTokenIssuer issuer) =>
      Results.Ok(new DevTokenResponse(issuer.Issue("Dev Admin", AdminPolicy.Role), "Dev Admin")))
      .WithTags("Development")
      .AllowAnonymous()
      .Produces<DevTokenResponse>();
    return app;
  }
}

public record DevTokenResponse(string AccessToken, string Name);
