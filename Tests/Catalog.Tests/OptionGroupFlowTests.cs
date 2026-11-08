using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Catalog.Tests;

public sealed class OptionGroupFlowTests
{
    [Fact]
    public async Task CrudNormalizesNamesAndSupportsSearchAndPagination()
    {
        using var app = await ItemTestApplication.Create();
        var id = await OptionTestData.Group(app, "  Topping  ");
        var detail = (await app.Client.GetFromJsonAsync<JsonElement>($"/api/option-groups/{id}")).GetProperty("result");
        Assert.Equal("Topping", detail.GetProperty("optionGroupName").GetString());
        Assert.Equal(HttpStatusCode.OK, (await app.Client.PutAsJsonAsync($"/api/option-groups/{id}", new
        {
            optionGroupName = "Extras"
        })).StatusCode);
        await OptionTestData.Group(app, "Size");
        var page = (await app.Client.GetFromJsonAsync<JsonElement>(
            "/api/option-groups?search=Extra&page=1&pageSize=1")).GetProperty("result");
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(id, page.GetProperty("items")[0].GetProperty("optionGroupId").GetGuid());
        Assert.Equal(HttpStatusCode.OK, (await app.Client.DeleteAsync($"/api/option-groups/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync($"/api/option-groups/{id}")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\nname")]
    public async Task InvalidNamesAreRejected(string? name)
    {
        using var app = await ItemTestApplication.Create();
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.PostAsJsonAsync("/api/option-groups", new
        {
            optionGroupName = name
        })).StatusCode);
    }

    [Fact]
    public async Task DuplicateNamesAndLongNamesAreRejectedWithoutChangingExistingGroup()
    {
        using var app = await ItemTestApplication.Create();
        var id = await OptionTestData.Group(app, "Size");
        await OptionTestData.Group(app, "Topping");
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put })
        {
            using var request = new HttpRequestMessage(method, method == HttpMethod.Post
                ? "/api/option-groups"
                : $"/api/option-groups/{id}")
            {
                Content = JsonContent.Create(new
                {
                    optionGroupName = " Topping "
                })
            };
            Assert.Equal(HttpStatusCode.Conflict, (await app.Client.SendAsync(request)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.PostAsJsonAsync("/api/option-groups", new
        {
            optionGroupName = new string('x', 256)
        })).StatusCode);
        await using var db = app.OpenDatabase();
        Assert.Equal("Size", (await db.OptionGroups.SingleAsync(group => group.OptionGroupId == id)).OptionGroupName);
        Assert.Equal(2, await db.OptionGroups.CountAsync());
    }

    [Fact]
    public async Task OptionsAndItemAssignmentsPreventGroupDeletion()
    {
        using var app = await ItemTestApplication.Create();
        var withOptions = await OptionTestData.Group(app, "Size");
        await OptionTestData.Option(app, withOptions);
        Assert.Equal(HttpStatusCode.Conflict, (await app.Client.DeleteAsync($"/api/option-groups/{withOptions}")).StatusCode);
        var assigned = await OptionTestData.Group(app);
        Assert.Equal(HttpStatusCode.Created,
            (await OptionTestData.Assign(app, await OptionTestData.Item(app), assigned)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await app.Client.DeleteAsync($"/api/option-groups/{assigned}")).StatusCode);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=2147483647&pageSize=100")]
    public async Task InvalidPaginationIsRejected(string query)
    {
        using var app = await ItemTestApplication.Create();
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.GetAsync("/api/option-groups?" + query)).StatusCode);
    }
}
