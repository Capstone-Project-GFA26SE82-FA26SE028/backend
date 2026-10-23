using Customer.DAL.Interfaces;
using Customer.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Customer.DAL.Implements;

public sealed class FeedbackRepository(CustomerDbContext db) : IFeedbackRepository
{
    public Task<FeedbackAccess?> GetAccessByTokenHashAsync(string tokenHash, CancellationToken ct)
    {
        return db.FeedbackAccesses
            .FirstOrDefaultAsync(a => a.TokenHash == tokenHash, ct);
    }

    public Task<FeedbackAccess?> GetAccessByOrderAsync(Guid orderRef, CancellationToken ct)
    {
        return db.FeedbackAccesses
            .FirstOrDefaultAsync(a => a.OrderRef == orderRef, ct);
    }

    public Task<Feedback?> GetFeedbackByOrderAsync(Guid orderRef, CancellationToken ct)
    {
        return db.Feedbacks
            .Include(f => f.FeedbackItems)
            .Include(f => f.Customer)
            .FirstOrDefaultAsync(f => f.OrderRef == orderRef, ct);
    }

    public Task<Feedback?> GetFeedbackByIdAsync(Guid feedbackId, CancellationToken ct)
    {
        return db.Feedbacks
            .Include(f => f.FeedbackItems)
            .Include(f => f.Customer)
            .FirstOrDefaultAsync(f => f.FeedbackId == feedbackId, ct);
    }

    public async Task<(List<Feedback> Items, int Total)> GetPageAsync(
        int page,
        int pageSize,
        int? starFilter,
        CancellationToken ct)
    {
        var query = db.Feedbacks
            .AsNoTracking()
            .Include(f => f.FeedbackItems)
            .Include(f => f.Customer)
            .AsQueryable();

        if (starFilter.HasValue && starFilter.Value >= 1 && starFilter.Value <= 5)
        {
            query = query.Where(f => f.Star == starFilter.Value);
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(f => f.SubmittedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<(double AvgRating, int Total, int S5, int S4, int S3, int S2, int S1)> GetSummaryAsync(CancellationToken ct)
    {
        var total = await db.Feedbacks.CountAsync(ct);
        if (total == 0)
        {
            return (0.0, 0, 0, 0, 0, 0, 0);
        }

        var avg = await db.Feedbacks.AverageAsync(f => (double)f.Star, ct);
        var s5 = await db.Feedbacks.CountAsync(f => f.Star == 5, ct);
        var s4 = await db.Feedbacks.CountAsync(f => f.Star == 4, ct);
        var s3 = await db.Feedbacks.CountAsync(f => f.Star == 3, ct);
        var s2 = await db.Feedbacks.CountAsync(f => f.Star == 2, ct);
        var s1 = await db.Feedbacks.CountAsync(f => f.Star == 1, ct);

        return (Math.Round(avg, 2), total, s5, s4, s3, s2, s1);
    }

    public void CreateAccess(FeedbackAccess access)
    {
        db.FeedbackAccesses.Add(access);
    }

    public void CreateFeedback(Feedback feedback)
    {
        db.Feedbacks.Add(feedback);
    }
}

