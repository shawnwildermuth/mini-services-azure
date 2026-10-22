using System.Net;
using System.Net.Http.Json;
using Store.Api.Addresses;

namespace Store.Api.Tests;

public class AddressApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  private static object NewAddress(string name = "Ada Lovelace", string country = "us", bool isDefault = false) => new
  {
    fullName = name,
    line1 = "1 Main St",
    city = "Atlanta",
    region = "GA",
    postalCode = "30301",
    countryCode = country,
    isDefault,
  };

  private static async Task<AddressDto> Create(HttpClient client, object address)
  {
    var response = await client.PostAsJsonAsync("/api/addresses", address);
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    return (await response.Content.ReadFromJsonAsync<AddressDto>())!;
  }

  [Fact]
  public async Task Create_NormalizesCountryAndMakesFirstAddressDefault()
  {
    var user = await factory.CreateUserAsync();

    var first = await Create(user.Http, NewAddress(country: "ca"));
    var second = await Create(user.Http, NewAddress(name: "Second"));

    Assert.Equal("CA", first.CountryCode);
    Assert.True(first.IsDefault);
    Assert.False(second.IsDefault);
  }

  [Fact]
  public async Task Create_RejectsInvalidInput()
  {
    var user = await factory.CreateUserAsync();

    var badCountry = await user.Http.PostAsJsonAsync("/api/addresses", NewAddress(country: "USA"));
    var missingName = await user.Http.PostAsJsonAsync("/api/addresses", NewAddress(name: ""));

    Assert.Equal(HttpStatusCode.BadRequest, badCountry.StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, missingName.StatusCode);
  }

  [Fact]
  public async Task Create_EnforcesMaximumAddresses()
  {
    var user = await factory.CreateUserAsync();
    for (var i = 0; i < AddressEndpoints.MaxAddressesPerUser; i++)
      await Create(user.Http, NewAddress(name: $"Person {i}"));

    var response = await user.Http.PostAsJsonAsync("/api/addresses", NewAddress());

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task SettingNewDefault_ClearsPreviousDefault()
  {
    var user = await factory.CreateUserAsync();
    var first = await Create(user.Http, NewAddress());
    await Create(user.Http, NewAddress(name: "Second", isDefault: true));

    var list = await user.Http.GetFromJsonAsync<List<AddressDto>>("/api/addresses");

    Assert.Single(list!, a => a.IsDefault);
    Assert.False(list!.Single(a => a.Id == first.Id).IsDefault);
    Assert.Equal("Second", list![0].FullName); // default listed first
  }

  [Fact]
  public async Task Update_ChangesFields()
  {
    var user = await factory.CreateUserAsync();
    var address = await Create(user.Http, NewAddress());

    var response = await user.Http.PutAsJsonAsync($"/api/addresses/{address.Id}", NewAddress(name: "Renamed", country: "mx", isDefault: true));
    var updated = await response.Content.ReadFromJsonAsync<AddressDto>();

    Assert.Equal("Renamed", updated!.FullName);
    Assert.Equal("MX", updated.CountryCode);
  }

  [Fact]
  public async Task Delete_PromotesAnotherAddressToDefault()
  {
    var user = await factory.CreateUserAsync();
    var first = await Create(user.Http, NewAddress());
    var second = await Create(user.Http, NewAddress(name: "Second"));

    Assert.Equal(HttpStatusCode.NoContent, (await user.Http.DeleteAsync($"/api/addresses/{first.Id}")).StatusCode);

    var remaining = await user.Http.GetFromJsonAsync<AddressDto>($"/api/addresses/{second.Id}");
    Assert.True(remaining!.IsDefault);
  }

  [Fact]
  public async Task Addresses_AreInvisibleToOtherUsers()
  {
    var alice = await factory.CreateUserAsync();
    var bob = await factory.CreateUserAsync();
    var address = await Create(alice.Http, NewAddress());

    Assert.Equal(HttpStatusCode.NotFound, (await bob.Http.GetAsync($"/api/addresses/{address.Id}")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await bob.Http.PutAsJsonAsync($"/api/addresses/{address.Id}", NewAddress())).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await bob.Http.DeleteAsync($"/api/addresses/{address.Id}")).StatusCode);
    Assert.Empty((await bob.Http.GetFromJsonAsync<List<AddressDto>>("/api/addresses"))!);
  }
}
