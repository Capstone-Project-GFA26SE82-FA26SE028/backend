using Customer.Common.DTOs;
using Customer.Common.Exceptions;
using Xunit;

namespace Customer.Tests;

public sealed class FeedbackTests
{
    [Fact]
    public async Task SubmitFeedback_ValidPayload_CreatesFeedbackWithItems()
    {
        using var fixture = new CustomerTestFixture();

        var orderId = Guid.NewGuid();
        var item1 = Guid.NewGuid();
        var item2 = Guid.NewGuid();

        var result = await fixture.FeedbackService.SubmitFeedback(new SubmitFeedbackDTO
        {
            OrderRef = orderId,
            Star = 5,
            Comment = "Đồ ăn rất ngon và nóng sốt!",
            Items = new List<FeedbackItemDTO>
            {
                new FeedbackItemDTO { OrderItemRef = item1, Star = 5, Comment = "Cơm gà da giòn rụm" },
                new FeedbackItemDTO { OrderItemRef = item2, Star = 4, Comment = "Trà sữa đậm vị" }
            }
        }, default);

        Assert.NotNull(result);
        Assert.Equal(orderId, result.OrderRef);
        Assert.Equal(5, result.Star);
        Assert.Equal(2, result.Items.Count);

        var retrieved = await fixture.FeedbackService.GetByOrder(orderId, default);
        Assert.NotNull(retrieved);
        Assert.Equal("Đồ ăn rất ngon và nóng sốt!", retrieved.Comment);
    }

    [Fact]
    public async Task SubmitFeedback_DuplicateOrder_ThrowsConflictCustomerException()
    {
        using var fixture = new CustomerTestFixture();

        var orderId = Guid.NewGuid();

        await fixture.FeedbackService.SubmitFeedback(new SubmitFeedbackDTO
        {
            OrderRef = orderId,
            Star = 5,
            Comment = "Lần 1"
        }, default);

        // Submit again on same order
        var ex = await Assert.ThrowsAsync<CustomerException>(() => fixture.FeedbackService.SubmitFeedback(new SubmitFeedbackDTO
        {
            OrderRef = orderId,
            Star = 4,
            Comment = "Lần 2"
        }, default));

        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task SubmitFeedback_InvalidStar_ThrowsBadRequest()
    {
        using var fixture = new CustomerTestFixture();

        var orderId = Guid.NewGuid();

        var ex0 = await Assert.ThrowsAsync<CustomerException>(() => fixture.FeedbackService.SubmitFeedback(new SubmitFeedbackDTO
        {
            OrderRef = orderId,
            Star = 0
        }, default));

        Assert.Equal(400, ex0.StatusCode);

        var ex6 = await Assert.ThrowsAsync<CustomerException>(() => fixture.FeedbackService.SubmitFeedback(new SubmitFeedbackDTO
        {
            OrderRef = orderId,
            Star = 6
        }, default));

        Assert.Equal(400, ex6.StatusCode);
    }

    [Fact]
    public async Task GetSummary_CalculatesAverageAndDistributionAccurately()
    {
        using var fixture = new CustomerTestFixture();

        await fixture.FeedbackService.SubmitFeedback(new SubmitFeedbackDTO { OrderRef = Guid.NewGuid(), Star = 5 }, default);
        await fixture.FeedbackService.SubmitFeedback(new SubmitFeedbackDTO { OrderRef = Guid.NewGuid(), Star = 5 }, default);
        await fixture.FeedbackService.SubmitFeedback(new SubmitFeedbackDTO { OrderRef = Guid.NewGuid(), Star = 4 }, default);
        await fixture.FeedbackService.SubmitFeedback(new SubmitFeedbackDTO { OrderRef = Guid.NewGuid(), Star = 2 }, default);

        var summary = await fixture.FeedbackService.GetSummary(default);

        Assert.Equal(4, summary.TotalFeedbacks);
        // (5 + 5 + 4 + 2) / 4 = 16 / 4 = 4.0
        Assert.Equal(4.0, summary.AverageRating);
        Assert.Equal(2, summary.Star5Count);
        Assert.Equal(1, summary.Star4Count);
        Assert.Equal(0, summary.Star3Count);
        Assert.Equal(1, summary.Star2Count);
        Assert.Equal(0, summary.Star1Count);
    }
}

