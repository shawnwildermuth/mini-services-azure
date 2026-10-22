namespace Store.Api.Admin;

public static class AdminEndpoints
{
  /// <summary>Everything under /api/admin requires an Entra-issued token carrying the Admin role.</summary>
  public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
  {
    var admin = app.MapGroup("/api/admin").RequireAuthorization(AdminPolicy.Name);

    admin.MapAdminDashboardEndpoint();
    admin.MapAdminCatalogEndpoints();
    admin.MapAdminOrderEndpoints();
    admin.MapAdminOperationsEndpoints();

    app.MapDevTokenEndpoint();
    return app;
  }
}
