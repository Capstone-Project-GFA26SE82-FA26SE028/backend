using AI.Common.DTOs;

namespace AI.BLL.Interfaces;

public interface IRecommendationService
{
    Task<RecommendationResponseDTO> Recommend(RecommendationRequestDTO request, CancellationToken ct);
}
