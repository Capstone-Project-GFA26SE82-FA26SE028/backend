using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Catalog.BLL.Interfaces;
using Shared.Common.DTOs;
using Xunit;

namespace Catalog.Tests;

public sealed class ItemKitchenStationTests
{
    [Fact]
    public async Task AssignmentClearAndStaleVersionAreEnforced()
    {
        var lookup = new Lookup();
        using var app = await ItemTestApplication.Create(stationLookup: lookup);
        var item = await app.CreateItem(await app.AddCategory());
        var id = item.GetProperty("itemId").GetGuid();
        var version = item.GetProperty("version").GetString();
        var station = Guid.NewGuid();
        var response = await app.Client.PatchAsJsonAsync($"/api/items/{id}/kitchen-station", new
        {
            kitchenStationId = station,
            version
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var assigned = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        Assert.Equal(station, assigned.GetProperty("kitchenStationId").GetGuid());
        var stale = await app.Client.PatchAsJsonAsync($"/api/items/{id}/kitchen-station", new
        {
            kitchenStationId = (Guid?)null,
            version
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var cleared = await app.Client.PatchAsJsonAsync($"/api/items/{id}/kitchen-station", new
        {
            kitchenStationId = (Guid?)null,
            version = assigned.GetProperty("version").GetString()
        });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await cleared.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("result").GetProperty("kitchenStationId").ValueKind);
        Assert.Equal(1, lookup.Calls);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(503)]
    public async Task InvalidOrUnavailableStationDoesNotChangeItem(int code)
    {
        using var app = await ItemTestApplication.Create(stationLookup: new Lookup { Error = code });
        var item = await app.CreateItem(await app.AddCategory());
        var id = item.GetProperty("itemId").GetGuid();
        var response = await app.Client.PatchAsJsonAsync($"/api/items/{id}/kitchen-station", new
        {
            kitchenStationId = Guid.NewGuid(),
            version = item.GetProperty("version").GetString()
        });
        Assert.Equal(code, (int)response.StatusCode);
        var detail = await app.Client.GetFromJsonAsync<JsonElement>($"/api/items/{id}");
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("result").GetProperty("kitchenStationId").ValueKind);
        Assert.Equal(item.GetProperty("version").GetString(), detail.GetProperty("result").GetProperty("version").GetString());
    }

    [Theory]
    [InlineData("kitchen")]
    [InlineData("waiter")]
    [InlineData("admin")]
    public async Task NonManagerCannotAssign(string role)
    {
        var lookup = new Lookup();
        using var app = await ItemTestApplication.Create(stationLookup: lookup);
        var item = await app.CreateItem(await app.AddCategory());
        app.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", role);
        var response = await app.Client.PatchAsJsonAsync($"/api/items/{item.GetProperty("itemId").GetGuid()}/kitchen-station", new
        {
            kitchenStationId = Guid.NewGuid(),
            version = item.GetProperty("version").GetString()
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, lookup.Calls);
    }

    private sealed class Lookup : IKitchenStationLookup
    {
        public int? Error { get; set; }
        public int Calls { get; private set; }

        public Task<ResponseDTO?> ValidateAsync(Guid stationId, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Error.HasValue ? new ResponseDTO("Station unavailable", Error.Value) : null);
        }
    }
}
