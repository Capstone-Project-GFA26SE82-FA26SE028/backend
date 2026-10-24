using AI.BLL.Interfaces;
using AI.Common.DTOs;
using Backend.Contracts.AI;
using Grpc.Core;

namespace AI.API.GrpcServices;

public sealed class RecommendationGrpcService(IRecommendationService service) : RecommendationGrpc.RecommendationGrpcBase
{
    public override async Task<RecommendationReply> Recommend(RecommendationRequest request, ServerCallContext context)
    {
        var result = await service.Recommend(new RecommendationRequestDTO(
            AIGrpcMapping.Guest(request.Access), Guid.Parse(request.CartId), request.Limit), AIInterceptor.Cancellation(context));
        var reply = new RecommendationReply
        {
            CartId = result.CartId.ToString(),
            CartVersion = result.CartVersion,
            Message = result.Message,
            SearchLimited = result.SearchLimited,
            EvaluatedAt = result.EvaluatedAt.ToString("O")
        };
        reply.Items.AddRange(result.Items.Select(item => new SuggestedItem
        {
            Item = AIGrpcMapping.Card(item.Item),
            Reason = item.Reason
        }));
        return reply;
    }
}
