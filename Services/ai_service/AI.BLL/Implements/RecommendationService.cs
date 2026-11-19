using AI.BLL.Interfaces;
using AI.Common.DTOs;
using AI.Common.Exceptions;
using AI.Common.Settings;
using Microsoft.Extensions.Options;

namespace AI.BLL.Implements;

public sealed class RecommendationService(
    IGuestAccessClient guests,
    ICartClient carts,
    MenuContextReader menu,
    IAssistantModelClient model,
    IOptions<AISettings> options,
    TimeProvider clock) : IRecommendationService
{
    public async Task<RecommendationResponseDTO> Recommend(RecommendationRequestDTO request, CancellationToken ct)
    {
        AssistantRules.ValidateGuest(request.Guest);
        var settings = options.Value;
        if (request.CartId == Guid.Empty || request.Limit < 1 || request.Limit > settings.MaximumSuggestions)
        {
            throw new AIException(400, "A valid cart ID and a recommendation limit within the configured maximum are required.");
        }

        if (settings.DuplicatePolicy != "ItemId")
        {
            throw new AIException(503, "Configure the approved ItemId recommendation duplicate policy.");
        }

        var guest = await guests.VerifyAsync(request.Guest, ct);
        var cart = await carts.GetByIdAsync(request.Guest, guest.RestaurantId, request.CartId, ct);
        AssistantRules.ValidateCart(cart, request.CartId, guest, clock.GetUtcNow());
        if (cart.Items.Count == 0)
        {
            return Result(cart, "Bạn hãy thêm món vào giỏ để nhận gợi ý món ăn kèm hoặc đồ uống.", [], false);
        }

        var source = await menu.Read(request.Guest, guest.RestaurantId, ct);
        var excluded = cart.Items.Select(line => line.ItemId).ToHashSet();
        var cartContext = new List<ModelCartLineDTO>();
        foreach (var line in cart.Items)
        {
            var item = source.Items.FirstOrDefault(item => item.ItemId == line.ItemId)
                ?? await menu.Refresh(request.Guest, guest.RestaurantId, line.ItemId, ct);
            if (item is null)
            {
                throw new AIException(409, "Một món trong giỏ không còn trong thực đơn hiện tại. Bạn vui lòng làm mới giỏ.");
            }

            cartContext.Add(new ModelCartLineDTO(item.ItemName, item.CategoryName, line.Quantity));
        }

        var eligible = source.Items.Where(item => AssistantRules.CanRecommend(item) && !excluded.Contains(item.ItemId)).ToArray();
        var candidates = menu.Candidates(eligible, string.Join(" ", cartContext.Select(line => line.Category + " " + line.Name)));
        var limited = source.TotalCount > source.Items.Count || eligible.Length > candidates.Count;
        if (candidates.Count == 0)
        {
            return Result(cart, "Chưa có món ăn kèm hoặc đồ uống phù hợp có thể đặt trong phần thực đơn hiện tại.", [], limited);
        }

        var keyed = AssistantRules.Keyed(candidates);
        var suggestions = await model.RecommendAsync(new RecommendationModelRequestDTO(cartContext, AssistantRules.ModelItems(keyed), request.Limit), ct);
        if (suggestions is null || suggestions.Count > request.Limit || suggestions.Any(choice => choice is null
                || string.IsNullOrWhiteSpace(choice.Key) || !keyed.ContainsKey(choice.Key) || !RecommendationReasons.Allowed.Contains(choice.Reason, StringComparer.Ordinal))
            || suggestions.Select(choice => choice.Key).Distinct(StringComparer.Ordinal).Count() != suggestions.Count)
        {
            throw new AIException(502, "The model returned invalid or duplicate recommendations.");
        }

        AssistantRules.RequireSameGuest(guest, await guests.VerifyAsync(request.Guest, ct));
        var refreshedCart = await carts.GetByIdAsync(request.Guest, guest.RestaurantId, request.CartId, ct);
        AssistantRules.ValidateCart(refreshedCart, request.CartId, guest, clock.GetUtcNow());
        if (refreshedCart.Version != cart.Version || !SameItems(cart.Items, refreshedCart.Items))
        {
            throw new AIException(409, "Giỏ hàng đã thay đổi trong lúc tạo gợi ý. Bạn vui lòng lấy gợi ý mới.");
        }

        var selected = new List<SuggestedItemDTO>();
        foreach (var suggestion in suggestions)
        {
            var item = await menu.Refresh(request.Guest, guest.RestaurantId, keyed[suggestion.Key].ItemId, ct);
            if (item is not null && AssistantRules.CanRecommend(item) && !excluded.Contains(item.ItemId)
                && selected.All(choice => choice.Item.ItemId != item.ItemId))
            {
                selected.Add(new SuggestedItemDTO(item, suggestion.Reason));
            }
        }

        return Result(refreshedCart, selected.Count == 0
            ? "Chưa có gợi ý phù hợp có thể đặt. Bạn vui lòng làm mới giỏ để xem lại."
            : "Bạn có thể thêm các món gợi ý vào giỏ. Hãy chọn đủ tùy chọn bắt buộc trước khi thêm.", selected, limited);
    }

    private static bool SameItems(IReadOnlyList<CartLineDTO> first, IReadOnlyList<CartLineDTO> second)
    {
        return first.OrderBy(line => line.ItemId).ThenBy(line => line.Quantity)
            .SequenceEqual(second.OrderBy(line => line.ItemId).ThenBy(line => line.Quantity));
    }

    private RecommendationResponseDTO Result(CartContextDTO cart, string message, IReadOnlyList<SuggestedItemDTO> items, bool limited)
    {
        return new RecommendationResponseDTO(cart.CartId, cart.Version, message, items, limited, clock.GetUtcNow());
    }
}
