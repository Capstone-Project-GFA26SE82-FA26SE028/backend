using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Catalog.Tests;

internal static class OptionTestData
{
    public static async Task<Guid> Group(ItemTestApplication app, string name = "Topping")
    {
        var response = await app.Client.PostAsJsonAsync("/api/option-groups", new
        {
            optionGroupName = name
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        var id = result.GetProperty("optionGroupId").GetGuid();
        Assert.Equal($"/api/option-groups/{id}", response.Headers.Location?.OriginalString);
        return id;
    }

    public static async Task<Guid> Option(ItemTestApplication app, Guid groupId, string name = "Extra egg", decimal price = 10000m)
    {
        var response = await app.Client.PostAsJsonAsync("/api/options", new
        {
            optionGroupId = groupId,
            optionName = name,
            extraPrice = price
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        Assert.Equal("Inactive", result.GetProperty("availability").GetString());
        Assert.Equal(price, result.GetProperty("extraPrice").GetDecimal());
        var id = result.GetProperty("optionId").GetGuid();
        Assert.Equal($"/api/options/{id}", response.Headers.Location?.OriginalString);
        return id;
    }

    public static async Task<Guid> Item(ItemTestApplication app, string name = "Soup")
    {
        var item = await app.CreateItem(await app.AddCategory(), name);
        return item.GetProperty("itemId").GetGuid();
    }

    public static Task<HttpResponseMessage> Assign(
        ItemTestApplication app, Guid itemId, Guid groupId, int min = 0, int max = 1)
    {
        return app.Client.PostAsJsonAsync($"/api/items/{itemId}/option-groups", new
        {
            optionGroupId = groupId,
            minSelection = min,
            maxSelection = max
        });
    }
}
