using Customer.Common.DTOs;

namespace Customer.BLL.Interfaces;

public interface IFeedbackService
{
    Task<string> CreateFeedbackAccess(Guid orderRef, CancellationToken ct);

    Task<FeedbackDTO> SubmitFeedback(SubmitFeedbackDTO request, CancellationToken ct);

    Task<FeedbackDTO?> GetByOrder(Guid orderRef, CancellationToken ct);

    Task<(List<FeedbackDTO> Items, int Total)> GetAll(int page, int pageSize, int? starFilter, CancellationToken ct);

    Task<FeedbackSummaryDTO> GetSummary(CancellationToken ct);
}

