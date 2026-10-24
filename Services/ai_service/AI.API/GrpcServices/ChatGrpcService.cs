using AI.BLL.Interfaces;
using AI.Common.DTOs;
using Backend.Contracts.AI;
using Grpc.Core;

namespace AI.API.GrpcServices;

public sealed class ChatGrpcService(IChatService service) : ChatGrpc.ChatGrpcBase
{
    public override async Task<ChatReply> Chat(ChatRequest request, ServerCallContext context)
    {
        var result = await service.Chat(new ChatRequestDTO(
            AIGrpcMapping.Guest(request.Access), request.Message,
            request.History.Select(turn => new AI.Common.DTOs.ConversationTurn(turn.Role, turn.Content)).ToArray()), AIInterceptor.Cancellation(context));
        var reply = new ChatReply
        {
            Answer = result.Answer,
            SearchLimited = result.SearchLimited,
            EvaluatedAt = result.EvaluatedAt.ToString("O")
        };
        reply.Items.AddRange(result.Items.Select(AIGrpcMapping.Card));
        return reply;
    }
}
