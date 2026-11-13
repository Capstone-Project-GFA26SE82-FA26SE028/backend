using AI.Common.DTOs;
using AI.Common.Exceptions;

namespace AI.Tests;

public sealed class RecommendationServiceTests
{
    [Theory]
    [InlineData("A suggested accompaniment to your current selection.", "Món ăn kèm được gợi ý cho các món bạn đã chọn.")]
    [InlineData("An additional menu choice to consider with your meal.", "Một lựa chọn thêm cho bữa ăn của bạn.")]
    [InlineData("A suggestion based on the dishes in your cart.", "Gợi ý dựa trên các món trong giỏ hàng của bạn.")]
    public async Task ControlledModelReasonsAreRenderedInVietnamese(string modelReason, string vietnameseReason)
    {
        var context = new AssistantTestContext
        {
            ForcedSuggestions = [new ModelSuggestionDTO { Key = "m0", Reason = modelReason }]
        };

        var result = await context.Recommendations().Recommend(context.Request(), default);

        Assert.Equal("Đã có gợi ý cho bạn. Vui lòng chọn các tùy chọn bắt buộc trước khi thêm món vào giỏ hàng.", result.Message);
        Assert.Equal(vietnameseReason, Assert.Single(result.Items).Reason);
        Assert.Equal(1, context.ModelCalls);
        Assert.Equal(2, context.CartReads);
    }

    [Fact]
    public async Task NoOrderableCandidatesReturnsVietnameseWithoutCallingModel()
    {
        var context = new AssistantTestContext();
        context.Menu[1].CanOrder = false;
        context.Menu[2].CanOrder = false;

        var result = await context.Recommendations().Recommend(context.Request(), default);

        Assert.Equal("Hiện chưa có món phù hợp có thể đặt thêm trong phạm vi thực đơn đã tìm kiếm.", result.Message);
        Assert.Empty(result.Items);
        Assert.Equal(0, context.ModelCalls);
    }

    [Fact]
    public async Task RealCartExcludesEveryVariantOfSameItemAndRechecksPrice()
    {
        var context = new AssistantTestContext();
        context.Cart = context.Cart with { Items = [new CartLineDTO(context.Menu[0].ItemId, 1), new CartLineDTO(context.Menu[0].ItemId, 2)] };
        context.Menu[1].RequiresOptionSelection = true;
        context.AfterModel = () => context.Menu[1].Price = 18000;
        var result = await context.Recommendations().Recommend(context.Request(), default);
        Assert.Equal(2, context.CartReads);
        Assert.Equal(2, result.Items.Count);
        Assert.DoesNotContain(result.Items, item => item.Item.ItemId == context.Menu[0].ItemId);
        Assert.DoesNotContain(context.ModelRequest!.Candidates, item => item.Name == "Beef rice");
        Assert.Equal(18000, result.Items.Single(item => item.Item.ItemId == context.Menu[1].ItemId).Item.Price);
        Assert.True(result.Items.Single(item => item.Item.ItemId == context.Menu[1].ItemId).Item.RequiresOptionSelection);
    }

    [Fact]
    public async Task ChangedCartRequiresFreshSuggestions()
    {
        var context = new AssistantTestContext();
        context.AfterModel = () => context.Cart = context.Cart with { Version = "v2" };
        var error = await Assert.ThrowsAsync<AIException>(() => context.Recommendations().Recommend(context.Request(), default));
        Assert.Equal(409, error.StatusCode);
    }

    [Fact]
    public async Task OnlyOrderableCandidatesReachTheModelAndFreshAvailabilityWins()
    {
        var context = new AssistantTestContext();
        context.Menu[2].CanOrder = false;
        context.AfterModel = () => context.Menu[1].CanOrder = false;
        var result = await context.Recommendations().Recommend(context.Request(), default);
        Assert.Single(context.ModelRequest!.Candidates);
        Assert.Empty(result.Items);
        Assert.Equal("Các món được gợi ý hiện không còn nhận đặt. Vui lòng tải lại và thử lại.", result.Message);
    }

    [Theory]
    [InlineData("unknown", "A suggested accompaniment to your current selection.")]
    [InlineData("m0", "Guaranteed allergy-safe and free")]
    public async Task InvalidModelSelectionOrClaimsAreRejected(string key, string reason)
    {
        var context = new AssistantTestContext
        {
            ForcedSuggestions = [new ModelSuggestionDTO { Key = key, Reason = reason }]
        };
        var error = await Assert.ThrowsAsync<AIException>(() => context.Recommendations().Recommend(context.Request(), default));
        Assert.Equal(502, error.StatusCode);
    }

    [Fact]
    public async Task DuplicateModelSuggestionsAreRejected()
    {
        var choice = new ModelSuggestionDTO { Key = "m0", Reason = RecommendationReasons.Allowed[0] };
        var context = new AssistantTestContext { ForcedSuggestions = [choice, choice] };
        var error = await Assert.ThrowsAsync<AIException>(() => context.Recommendations().Recommend(context.Request(), default));
        Assert.Equal(502, error.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task RequestLimitIsBounded(int limit)
    {
        var context = new AssistantTestContext();
        var error = await Assert.ThrowsAsync<AIException>(() => context.Recommendations().Recommend(context.Request(limit), default));
        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task EmptyCartDoesNotCallModel()
    {
        var context = new AssistantTestContext();
        context.Cart = context.Cart with { Items = [] };
        var result = await context.Recommendations().Recommend(context.Request(), default);
        Assert.Empty(result.Items);
        Assert.Equal(0, context.ModelCalls);
        Assert.Equal("Hãy thêm món vào giỏ hàng trước khi yêu cầu gợi ý.", result.Message);
    }

    [Fact]
    public async Task CartFromAnotherSessionIsRejected()
    {
        var context = new AssistantTestContext();
        context.Cart = context.Cart with { TableSessionId = Guid.NewGuid() };
        var error = await Assert.ThrowsAsync<AIException>(() => context.Recommendations().Recommend(context.Request(), default));
        Assert.Equal(503, error.StatusCode);
        Assert.Equal(0, context.ModelCalls);
    }

    [Fact]
    public async Task ExpiredCartIsRejected()
    {
        var context = new AssistantTestContext();
        context.Cart = context.Cart with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
        var error = await Assert.ThrowsAsync<AIException>(() => context.Recommendations().Recommend(context.Request(), default));
        Assert.Equal(409, error.StatusCode);
    }
}
