using Microsoft.AspNetCore.Identity;
using Store.Data;

namespace Store.Api.Auth;

/// <summary>
/// Placeholder until a real email provider is wired up. Logs that a message would be sent; links and
/// codes are only written to the log in Development because they grant account access.
/// </summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger, IHostEnvironment environment) : IEmailSender<AppUser>
{
  public Task SendConfirmationLinkAsync(AppUser user, string email, string confirmationLink) =>
    Log("confirmation link", email, confirmationLink);

  public Task SendPasswordResetLinkAsync(AppUser user, string email, string resetLink) =>
    Log("password reset link", email, resetLink);

  public Task SendPasswordResetCodeAsync(AppUser user, string email, string resetCode) =>
    Log("password reset code", email, resetCode);

  private Task Log(string kind, string email, string secret)
  {
    if (environment.IsDevelopment())
      logger.LogInformation("[email stub] {Kind} for {Email}: {Secret}", kind, email, secret);
    else
      logger.LogInformation("[email stub] {Kind} requested (not delivered; no provider configured)", kind);
    return Task.CompletedTask;
  }
}
