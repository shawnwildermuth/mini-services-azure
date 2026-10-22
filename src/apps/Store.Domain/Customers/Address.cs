namespace Store.Domain.Customers;

/// <summary>A saved shipping address. <see cref="CountryCode"/> is ISO 3166-1 alpha-2 (drives carrier selection).</summary>
public class Address
{
  public int Id { get; set; }
  public required string UserId { get; set; }
  public required string FullName { get; set; }
  public required string Line1 { get; set; }
  public string? Line2 { get; set; }
  public required string City { get; set; }
  public string? Region { get; set; }
  public required string PostalCode { get; set; }
  public required string CountryCode { get; set; }
  public string? Phone { get; set; }
  public bool IsDefault { get; set; }
}
