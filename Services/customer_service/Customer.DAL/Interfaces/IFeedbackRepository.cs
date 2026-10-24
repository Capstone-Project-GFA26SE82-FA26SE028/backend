using Customer.DAL.Models;

namespace Customer.DAL.Interfaces;

public interface IFeedbackRepository
{
    Task<FeedbackAccess?> GetAccessByTokenHashAsync(string tokenHash, CancellationToken ct);

    Task<FeedbackAccess?> GetAccessByOrderAsync(Guid orderRef, CancellationToken ct);

    Task<Feedback?> GetFeedbackByOrderAsync(Guid orderRef, CancellationToken ct);

    Task<Feedback?> GetFeedbackByIdAsync(Guid feedbackId, CancellationToken ct);

    Task<(List<Feedback> Items, int Total)> GetPageAsync(int page, int pageSize, int? starFilter, CancellationToken ct);

    Task<(double AvgRating, int Total, int S5, int S4, int S3, int S2, int S1)> GetSummaryAsync(CancellationToken ct);

    void CreateAccess(FeedbackAccess access);

    void CreateFeedback(Feedback feedback);
}

