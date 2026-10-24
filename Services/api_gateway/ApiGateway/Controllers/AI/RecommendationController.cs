using ApiGateway.DTOs.AI;
using ApiGateway.Settings;
using Backend.Contracts.AI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ApiGateway.Controllers.AI;

[ApiController]
[Route("api/ai/table-sessions/{sessionId:guid}/carts/{cartId:guid}/recommendations")]
public sealed class RecommendationController(RecommendationGrpc.RecommendationGrpcClient client, IOptions<AIClientSettings> settings) : AssistantControllerBase(settings)
{
    [HttpPost]
    public Task<IActionResult> Recommend(Guid sessionId, Guid cartId, RecommendationRequestDTO body, CancellationToken ct)
    {
        return Execute(async options =>
        {
            var result = await client.RecommendAsync(new RecommendationRequest
            {
                Access = Access(sessionId),
                CartId = cartId.ToString(),
                Limit = body.Limit
            }, options);
            return new RecommendationResponseDTO(Guid.Parse(result.CartId), result.CartVersion, result.Message,
                result.Items.Select(item => new SuggestedItemDTO(Card(item.Item), item.Reason)).ToArray(),
                result.SearchLimited, DateTimeOffset.Parse(result.EvaluatedAt, System.Globalization.CultureInfo.InvariantCulture));
        }, ct);
    }
}
