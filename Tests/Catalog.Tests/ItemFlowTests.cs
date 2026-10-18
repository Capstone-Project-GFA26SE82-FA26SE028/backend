using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Catalog.Common.Exceptions;
using Catalog.DAL.Implements;
using Microsoft.EntityFrameworkCore;
using Shared.Persistence.UnitOfWorks;
using Catalog.DAL.Models;
using Xunit;

namespace Catalog.Tests;

public sealed class ItemFlowTests
{
    [Fact]
    public async Task CreateAndUpdatePreserveCodeAvailabilityAndKitchenAssignment()
    {
        using var application = await ItemTestApplication.Create();
        var categoryId = await application.AddCategory();
        var item = await application.CreateItem(categoryId, "  Vegetarian Soup  ");
        var id = item.GetProperty("itemId").GetGuid();
        Assert.Equal("vegetarian-soup", item.GetProperty("itemCode").GetString());
        Assert.Equal("Vegetarian Soup", item.GetProperty("itemName").GetString());
        Assert.Equal("A seasonal vegetable soup.", item.GetProperty("description").GetString());
        Assert.Equal("Main Courses", item.GetProperty("categoryName").GetString());
        Assert.Equal("Inactive", item.GetProperty("availability").GetString());
        Assert.Equal(8, Convert.FromBase64String(item.GetProperty("version").GetString()!).Length);

        var stationId = Guid.NewGuid();
        await using (var db = application.OpenDatabase())
        {
            var stored = await db.Items.SingleAsync();
            stored.KitchenStationRef = stationId;
            await db.SaveChangesAsync();
            Assert.Empty(await db.Prices.ToListAsync());
        }

        var detail = await application.Client.GetFromJsonAsync<JsonElement>($"/api/items/{id}");
        var version = detail.GetProperty("result").GetProperty("version").GetString();
        var response = await application.Client.PutAsJsonAsync($"/api/items/{id}", new
        {
            categoryId,
            itemName = "Seasonal Soup",
            version
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        Assert.Equal("vegetarian-soup", updated.GetProperty("itemCode").GetString());
        Assert.Equal("Inactive", updated.GetProperty("availability").GetString());
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("description").ValueKind);
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("imageUrl").ValueKind);
        Assert.NotEqual(version, updated.GetProperty("version").GetString());
        await using var verification = application.OpenDatabase();
        Assert.Equal(stationId, (await verification.Items.SingleAsync()).KitchenStationRef);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Soup\nSpecial")]
    [InlineData("---!!!")]
    public async Task InvalidNamesDoNotCreateItems(string name)
    {
        using var application = await ItemTestApplication.Create();
        var categoryId = await application.AddCategory();
        var response = await application.Client.PostAsJsonAsync("/api/items", new
        {
            categoryId,
            itemName = name
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = application.OpenDatabase();
        Assert.Empty(await db.Items.ToListAsync());
    }

    [Fact]
    public async Task FieldLengthsAndImageSchemeAreValidated()
    {
        using var application = await ItemTestApplication.Create();
        var categoryId = await application.AddCategory();
        var bodies = new object[]
        {
            new { categoryId, itemName = new string('a', 256) },
            new { categoryId, itemName = new string('a', 51) },
            new { categoryId, itemName = "Soup", description = new string('a', 2001) },
            new { categoryId, itemName = "Soup", imageUrl = "file:///soup.png" },
            new { categoryId, itemName = "Soup", imageUrl = "/soup.png" },
            new { categoryId, itemName = "Soup", imageUrl = "https://example.test/" + new string('a', 2048) }
        };
        foreach (var body in bodies)
        {
            Assert.Equal(HttpStatusCode.BadRequest,
                (await application.Client.PostAsJsonAsync("/api/items", body)).StatusCode);
        }
    }

    [Fact]
    public async Task MissingAndInactiveCategoriesAreRejected()
    {
        using var application = await ItemTestApplication.Create();
        var inactiveId = await application.AddCategory(active: false);
        Assert.Equal(HttpStatusCode.NotFound,
            (await application.Client.PostAsJsonAsync("/api/items", new
            {
                categoryId = Guid.NewGuid(),
                itemName = "Soup"
            })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await application.Client.PostAsJsonAsync("/api/items", new
            {
                categoryId = inactiveId,
                itemName = "Soup"
            })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await application.Client.PostAsJsonAsync("/api/items", new
            {
                categoryId = Guid.Empty,
                itemName = "Soup"
            })).StatusCode);
    }

    [Fact]
    public async Task DuplicateNameAndSlugAreRejectedAcrossApplicableScopes()
    {
        using var application = await ItemTestApplication.Create();
        var firstCategory = await application.AddCategory();
        var secondCategory = await application.AddCategory("Starters");
        await application.CreateItem(firstCategory, "Soup Special");
        Assert.Equal(HttpStatusCode.Conflict,
            (await application.Client.PostAsJsonAsync("/api/items", new
            {
                categoryId = firstCategory,
                itemName = "  SOUP SPECIAL  "
            })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await application.Client.PostAsJsonAsync("/api/items", new
            {
                categoryId = secondCategory,
                itemName = "Soup-Special"
            })).StatusCode);
    }

    [Fact]
    public async Task ListFiltersByCategorySearchAndAvailabilityAndSupportsEmptyCategory()
    {
        using var application = await ItemTestApplication.Create();
        var first = await application.AddCategory();
        var second = await application.AddCategory("Drinks");
        var empty = await application.AddCategory("Empty", false);
        await application.CreateItem(first, "Tomato Soup");
        await application.CreateItem(first, "Pumpkin Soup");
        await application.CreateItem(second, "Coffee");

        var list = await application.Client.GetFromJsonAsync<JsonElement>(
            $"/api/items?categoryId={first}&search=Soup&availability=Inactive&page=2&pageSize=1");
        var page = list.GetProperty("result");
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, page.GetProperty("page").GetInt32());
        Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(first, page.GetProperty("items")[0].GetProperty("categoryId").GetGuid());
        var emptyList = await application.Client.GetFromJsonAsync<JsonElement>($"/api/items?categoryId={empty}");
        Assert.Equal(0, emptyList.GetProperty("result").GetProperty("totalCount").GetInt32());
        var all = await application.Client.GetFromJsonAsync<JsonElement>("/api/items");
        Assert.Equal(3, all.GetProperty("result").GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("?page=0", HttpStatusCode.BadRequest)]
    [InlineData("?pageSize=101", HttpStatusCode.BadRequest)]
    [InlineData("?page=2147483647&pageSize=100", HttpStatusCode.BadRequest)]
    [InlineData("?categoryId=bad-id", HttpStatusCode.BadRequest)]
    [InlineData("?categoryId=00000000-0000-0000-0000-000000000000", HttpStatusCode.BadRequest)]
    public async Task InvalidListQueriesAreRejected(string query, HttpStatusCode expected)
    {
        using var application = await ItemTestApplication.Create();
        Assert.Equal(expected, (await application.Client.GetAsync("/api/items" + query)).StatusCode);
    }

    [Fact]
    public async Task MissingItemsAndCategoriesReturnNotFound()
    {
        using var application = await ItemTestApplication.Create();
        Assert.Equal(HttpStatusCode.NotFound,
            (await application.Client.GetAsync($"/api/items/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await application.Client.GetAsync($"/api/items?categoryId={Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await application.Client.GetAsync("/api/items/invalid-id")).StatusCode);
    }

    [Fact]
    public async Task UpdateRejectsMissingMalformedAndStaleVersions()
    {
        using var application = await ItemTestApplication.Create();
        var categoryId = await application.AddCategory();
        var item = await application.CreateItem(categoryId);
        var id = item.GetProperty("itemId").GetGuid();
        foreach (var invalid in new string?[] { null, string.Empty, "invalid", Convert.ToBase64String([1]) })
        {
            Assert.Equal(HttpStatusCode.BadRequest,
                (await application.Client.PutAsJsonAsync($"/api/items/{id}", new
                {
                    categoryId,
                    itemName = "Changed Soup",
                    version = invalid
                })).StatusCode);
        }

        var original = item.GetProperty("version").GetString();
        Assert.Equal(HttpStatusCode.OK,
            (await application.Client.PutAsJsonAsync($"/api/items/{id}", new
            {
                categoryId,
                itemName = "First Update",
                version = original
            })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await application.Client.PutAsJsonAsync($"/api/items/{id}", new
            {
                categoryId,
                itemName = "Stale Update",
                version = original
            })).StatusCode);
        await using var db = application.OpenDatabase();
        Assert.Equal("First Update", (await db.Items.SingleAsync()).ItemName);
    }

    [Fact]
    public async Task UpdateAllowsInactiveCurrentCategoryButRejectsInactiveDestinationAndDuplicateName()
    {
        using var application = await ItemTestApplication.Create();
        var categoryId = await application.AddCategory();
        var destination = await application.AddCategory("Destination", false);
        var item = await application.CreateItem(categoryId);
        var id = item.GetProperty("itemId").GetGuid();
        var version = item.GetProperty("version").GetString();
        Assert.Equal(HttpStatusCode.Conflict,
            (await application.Client.PutAsJsonAsync($"/api/items/{id}", new
            {
                categoryId = destination,
                itemName = "Updated Soup",
                version
            })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await application.Client.PutAsJsonAsync($"/api/items/{id}", new
            {
                categoryId = Guid.NewGuid(),
                itemName = "Updated Soup",
                version
            })).StatusCode);

        await application.CreateItem(categoryId, "Existing Soup");
        Assert.Equal(HttpStatusCode.Conflict,
            (await application.Client.PutAsJsonAsync($"/api/items/{id}", new
            {
                categoryId,
                itemName = "Existing Soup",
                version
            })).StatusCode);

        await using (var db = application.OpenDatabase())
        {
            (await db.Categories.SingleAsync(category => category.CategoryId == categoryId)).IsActive = false;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK,
            (await application.Client.PutAsJsonAsync($"/api/items/{id}", new
            {
                categoryId,
                itemName = "Updated Soup",
                version
            })).StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("revoked", HttpStatusCode.Unauthorized)]
    [InlineData("ordinary-user", HttpStatusCode.Forbidden)]
    public async Task UnauthorizedUsersCannotReadOrWriteItems(string? token, HttpStatusCode expected)
    {
        using var application = await ItemTestApplication.Create();
        var categoryId = await application.AddCategory();
        application.Client.DefaultRequestHeaders.Authorization = token is null
            ? null
            : new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(expected, (await application.Client.GetAsync("/api/items")).StatusCode);
        Assert.Equal(expected, (await application.Client.PostAsJsonAsync("/api/items", new
        {
            categoryId,
            itemName = "Unauthorized Soup"
        })).StatusCode);
        await using var db = application.OpenDatabase();
        Assert.Empty(await db.Items.ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("UnconfirmedStatus")]
    public async Task UnconfiguredOrSellingInitialStatusCannotCreateItems(string status)
    {
        using var application = await ItemTestApplication.Create(status);
        var categoryId = await application.AddCategory();
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await application.Client.PostAsJsonAsync("/api/items", new
            {
                categoryId,
                itemName = "Soup"
            })).StatusCode);
        await using var db = application.OpenDatabase();
        Assert.Empty(await db.Items.ToListAsync());
    }

    [Fact]
    public async Task RepositoryConcurrencyTokenRejectsAnUpdateAfterTheEntityWasLoaded()
    {
        using var application = await ItemTestApplication.Create();
        var categoryId = await application.AddCategory();
        var created = await application.CreateItem(categoryId);
        var id = created.GetProperty("itemId").GetGuid();
        await using var staleContext = application.OpenDatabase();
        var repository = new ItemRepository(staleContext);
        var stale = (await repository.GetByIdAsync(id, CancellationToken.None))!;
        var originalVersion = stale.Version.ToArray();

        await using (var newerContext = application.OpenDatabase())
        {
            var newer = await newerContext.Items.SingleAsync();
            newer.ItemName = "Newer Update";
            await newerContext.SaveChangesAsync();
        }

        using var unitOfWork = new UnitOfWork<CatalogDbContext>(staleContext);
        var transaction = new ItemTransaction(unitOfWork);
        var exception = await Assert.ThrowsAsync<ItemPersistenceException>(() => transaction.ExecuteAsync(() =>
        {
            repository.SetOriginalVersion(stale, originalVersion);
            stale.ItemName = "Stale Update";
            return Task.FromResult(true);
        }, CancellationToken.None));
        Assert.Equal(ItemPersistenceFailure.ConcurrentUpdate, exception.Failure);
        await using var verification = application.OpenDatabase();
        Assert.Equal("Newer Update", (await verification.Items.SingleAsync()).ItemName);
    }
}
