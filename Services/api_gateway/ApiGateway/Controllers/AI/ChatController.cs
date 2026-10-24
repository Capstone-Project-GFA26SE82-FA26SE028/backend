using ApiGateway.DTOs.AI;
using ApiGateway.Settings;
using Backend.Contracts.AI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ApiGateway.Controllers.AI;

[ApiController]
[Route("api/ai/table-sessions/{sessionId:guid}/chat")]
public sealed class ChatController(ChatGrpc.ChatGrpcClient client, IOptions<AIClientSettings> settings) : AssistantControllerBase(settings)
{
    [HttpPost]
    public Task<IActionResult> Chat(Guid sessionId, ChatRequestDTO body, CancellationToken ct)
    {
        if (body.History is null || body.History.Any(turn => turn is null))
        {
            return Task.FromResult<IActionResult>(BadRequest(new Shared.Common.DTOs.ResponseDTO("Provide a valid conversation history.", 400)));
        }

        return Execute(async options =>
        {
            var request = new ChatRequest
            {
                Access = Access(sessionId),
                Message = body.Message
            };
            request.History.AddRange(body.History.Select(turn => new ConversationTurn
            {
                Role = turn.Role,
                Content = turn.Content
            }));
            var result = await client.ChatAsync(request, options);
            return new ChatResponseDTO(result.Answer, result.Items.Select(Card).ToArray(), result.SearchLimited, DateTimeOffset.Parse(result.EvaluatedAt, System.Globalization.CultureInfo.InvariantCulture));
        }, ct);
    }
}
