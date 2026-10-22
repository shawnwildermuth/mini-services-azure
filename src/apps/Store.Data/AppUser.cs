using Microsoft.AspNetCore.Identity;

namespace Store.Data;

/// <summary>A storefront customer. Admins authenticate through Entra ID instead and have no row here.</summary>
public class AppUser : IdentityUser;
