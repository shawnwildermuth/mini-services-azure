using System.Security.Claims;

namespace Store.Api.Admin;

/// <summary>
/// One structured log line per administrative change ("who did what to what"), sent to Application Insights like every
/// other log. Searchable by the logger name <c>Admin.Audit</c>. Never include customer data here: identify things by
/// SKU, order number or id.
/// </summary>
public static class AdminAudit
{
  public static string WhoIs(ClaimsPrincipal user) =>
    $"{user.FindFirstValue("name") ?? "unknown"} ({user.FindFirstValue("oid") ?? user.FindFirstValue("sub") ?? "no id"})";

  public static void AdminAction(this ILoggerFactory logs, ClaimsPrincipal user, string action, string target) =>
    logs.CreateLogger("Admin.Audit").LogInformation("Admin {Admin} {Action}: {Target}", WhoIs(user), action, target);
}
