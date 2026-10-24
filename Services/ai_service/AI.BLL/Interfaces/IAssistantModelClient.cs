using AI.Common.DTOs;

namespace AI.BLL.Interfaces;

public interface IAssistantModelClient
{
    Task<ChatPlanDTO> ChatAsync(ChatModelRequestDTO request, CancellationToken ct);

    Task<IReadOnlyList<ModelSuggestionDTO>> RecommendAsync(RecommendationModelRequestDTO request, CancellationToken ct);

    Task<bool> IsReadyAsync(CancellationToken ct);
}
