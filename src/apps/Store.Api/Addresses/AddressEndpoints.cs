using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Store.Api.Auth;
using Store.Data;
using Store.Domain.Customers;

namespace Store.Api.Addresses;

public record AddressDto(
  int Id, string FullName, string Line1, string? Line2, string City, string? Region,
  string PostalCode, string CountryCode, string? Phone, bool IsDefault);

public class AddressRequest
{
  [Required, StringLength(128)] public string FullName { get; set; } = "";
  [Required, StringLength(200)] public string Line1 { get; set; } = "";
  [StringLength(200)] public string? Line2 { get; set; }
  [Required, StringLength(100)] public string City { get; set; } = "";
  [StringLength(100)] public string? Region { get; set; }
  [Required, StringLength(20)] public string PostalCode { get; set; } = "";
  [Required, RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Must be a two-letter ISO country code.")]
  public string CountryCode { get; set; } = "";
  [StringLength(32)] public string? Phone { get; set; }
  public bool IsDefault { get; set; }
}

public static class AddressEndpoints
{
  public const int MaxAddressesPerUser = 10;

  public static IEndpointRouteBuilder MapAddressEndpoints(this IEndpointRouteBuilder app)
  {
    var group = app.MapGroup("/api/addresses").WithTags("Addresses").RequireAuthorization();

    group.MapGet("/", async (ClaimsPrincipal user, StoreDbContext db, CancellationToken ct) =>
      Results.Ok(await db.Addresses.AsNoTracking()
        .Where(a => a.UserId == user.GetUserId())
        .OrderByDescending(a => a.IsDefault).ThenBy(a => a.Id)
        .Select(a => ToDto(a))
        .ToListAsync(ct)))
      .WithName("GetAddresses")
      .Produces<List<AddressDto>>();

    group.MapGet("/{id:int}", async (int id, ClaimsPrincipal user, StoreDbContext db, CancellationToken ct) =>
      await db.Addresses.AsNoTracking().Where(a => a.Id == id && a.UserId == user.GetUserId())
        .Select(a => ToDto(a)).FirstOrDefaultAsync(ct) is { } address
        ? Results.Ok(address)
        : Results.NotFound())
      .WithName("GetAddress")
      .Produces<AddressDto>()
      .ProducesProblem(StatusCodes.Status404NotFound);

    group.MapPost("/", async (AddressRequest request, ClaimsPrincipal user, StoreDbContext db, CancellationToken ct) =>
    {
      var userId = user.GetUserId();
      var existing = await db.Addresses.Where(a => a.UserId == userId).ToListAsync(ct);
      if (existing.Count >= MaxAddressesPerUser)
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
          ["address"] = [$"You can save at most {MaxAddressesPerUser} addresses."],
        });

      var address = new Address { UserId = userId, FullName = "", Line1 = "", City = "", PostalCode = "", CountryCode = "" };
      Apply(address, request);
      // The first address is always the default.
      address.IsDefault = request.IsDefault || existing.Count == 0;
      if (address.IsDefault) existing.ForEach(a => a.IsDefault = false);

      db.Addresses.Add(address);
      await db.SaveChangesAsync(ct);
      return Results.Created($"/api/addresses/{address.Id}", ToDto(address));
    })
    .WithName("CreateAddress")
    .Produces<AddressDto>(StatusCodes.Status201Created)
    .ProducesValidationProblem();

    group.MapPut("/{id:int}", async (int id, AddressRequest request, ClaimsPrincipal user, StoreDbContext db, CancellationToken ct) =>
    {
      var userId = user.GetUserId();
      var all = await db.Addresses.Where(a => a.UserId == userId).ToListAsync(ct);
      var address = all.FirstOrDefault(a => a.Id == id);
      if (address is null) return Results.NotFound();

      Apply(address, request);
      if (request.IsDefault)
      {
        all.ForEach(a => a.IsDefault = a.Id == id);
      }
      else if (address.IsDefault)
      {
        // Can't unset the default directly; pick another address as default instead.
        address.IsDefault = true;
      }

      await db.SaveChangesAsync(ct);
      return Results.Ok(ToDto(address));
    })
    .WithName("UpdateAddress")
    .Produces<AddressDto>()
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status404NotFound);

    group.MapDelete("/{id:int}", async (int id, ClaimsPrincipal user, StoreDbContext db, CancellationToken ct) =>
    {
      var userId = user.GetUserId();
      var all = await db.Addresses.Where(a => a.UserId == userId).OrderBy(a => a.Id).ToListAsync(ct);
      var address = all.FirstOrDefault(a => a.Id == id);
      if (address is null) return Results.NotFound();

      db.Addresses.Remove(address);
      if (address.IsDefault && all.FirstOrDefault(a => a.Id != id) is { } next)
        next.IsDefault = true;

      await db.SaveChangesAsync(ct);
      return Results.NoContent();
    })
    .WithName("DeleteAddress")
    .Produces(StatusCodes.Status204NoContent)
    .ProducesProblem(StatusCodes.Status404NotFound);

    return app;
  }

  private static void Apply(Address address, AddressRequest request)
  {
    address.FullName = request.FullName.Trim();
    address.Line1 = request.Line1.Trim();
    address.Line2 = string.IsNullOrWhiteSpace(request.Line2) ? null : request.Line2.Trim();
    address.City = request.City.Trim();
    address.Region = string.IsNullOrWhiteSpace(request.Region) ? null : request.Region.Trim();
    address.PostalCode = request.PostalCode.Trim();
    address.CountryCode = request.CountryCode.Trim().ToUpperInvariant();
    address.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
  }

  private static AddressDto ToDto(Address a) =>
    new(a.Id, a.FullName, a.Line1, a.Line2, a.City, a.Region, a.PostalCode, a.CountryCode, a.Phone, a.IsDefault);
}
