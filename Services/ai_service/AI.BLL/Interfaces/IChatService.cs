using AI.Common.DTOs;

namespace AI.BLL.Interfaces;

public interface IChatService
{
    Task<ChatResponseDTO> Chat(ChatRequestDTO request, CancellationToken ct);
}
