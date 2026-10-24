using System.Security.Cryptography;
using System.Text;
using Customer.BLL.Interfaces;
using Customer.Common.DTOs;
using Customer.Common.Exceptions;
using Customer.DAL.Interfaces;
using Customer.DAL.Models;

namespace Customer.BLL.Implements;

public sealed class FeedbackService(
    IFeedbackRepository feedbacks,
    ICustomerRepository customers,
    ICustomerTransaction transaction,
    TimeProvider clock) : IFeedbackService
{
    public async Task<string> CreateFeedbackAccess(Guid orderRef, CancellationToken ct)
    {
        if (orderRef == Guid.Empty)
        {
            throw new CustomerException(400, "Invalid order reference.");
        }

        var existing = await feedbacks.GetAccessByOrderAsync(orderRef, ct);
        if (existing is not null && existing.ExpiredAt > clock.GetUtcNow().UtcDateTime)
        {
            return existing.TokenHash;
        }

        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var tokenHash = HashToken(rawToken);
        var now = clock.GetUtcNow().UtcDateTime;

        var access = new FeedbackAccess
        {
            FeedbackAccessId = Guid.NewGuid(),
            OrderRef = orderRef,
            TokenHash = tokenHash,
            ExpiredAt = now.AddDays(7),
            UsedAt = null
        };

        feedbacks.CreateAccess(access);
        return rawToken;
    }

    public async Task<FeedbackDTO> SubmitFeedback(SubmitFeedbackDTO request, CancellationToken ct)
    {
        if (request is null)
        {
            throw new CustomerException(400, "Feedback payload is required.");
        }

        if (request.OrderRef == Guid.Empty)
        {
            throw new CustomerException(400, "Order reference is required.");
        }

        if (request.Star < 1 || request.Star > 5)
        {
            throw new CustomerException(400, "Overall star rating must be between 1 and 5.");
        }

        return await transaction.ExecuteAsync(async () =>
        {
            var alreadyReviewed = await feedbacks.GetFeedbackByOrderAsync(request.OrderRef, ct);
            if (alreadyReviewed is not null)
            {
                throw new CustomerException(409, "This order has already been reviewed.");
            }

            var access = await feedbacks.GetAccessByOrderAsync(request.OrderRef, ct);
            var now = clock.GetUtcNow().UtcDateTime;

            if (access is null)
            {
                // Auto create access record for this order
                access = new FeedbackAccess
                {
                    FeedbackAccessId = Guid.NewGuid(),
                    OrderRef = request.OrderRef,
                    TokenHash = HashToken(request.OrderRef.ToString()),
                    ExpiredAt = now.AddDays(7),
                    UsedAt = now
                };
                feedbacks.CreateAccess(access);
            }
            else
            {
                access.UsedAt = now;
            }

            string? customerName = null;
            if (request.CustomerId.HasValue && request.CustomerId != Guid.Empty)
            {
                var customer = await customers.GetByIdAsync(request.CustomerId.Value, ct);
                customerName = customer?.CustomerName;
            }

            var feedback = new Feedback
            {
                FeedbackId = Guid.NewGuid(),
                AccessId = access.FeedbackAccessId,
                OrderRef = request.OrderRef,
                CustomerId = request.CustomerId,
                Star = request.Star,
                Comment = request.Comment?.Trim(),
                SubmittedAt = now
            };

            if (request.Items is not null && request.Items.Count > 0)
            {
                foreach (var item in request.Items)
                {
                    if (item.OrderItemRef != Guid.Empty && item.Star >= 1 && item.Star <= 5)
                    {
                        feedback.FeedbackItems.Add(new FeedbackItem
                        {
                            FeedbackItemId = Guid.NewGuid(),
                            FeedbackId = feedback.FeedbackId,
                            OrderItemRef = item.OrderItemRef,
                            Star = item.Star,
                            Comment = item.Comment?.Trim()
                        });
                    }
                }
            }

            feedbacks.CreateFeedback(feedback);

            return new FeedbackDTO
            {
                FeedbackId = feedback.FeedbackId,
                OrderRef = feedback.OrderRef,
                CustomerId = feedback.CustomerId,
                CustomerName = customerName,
                Star = feedback.Star,
                Comment = feedback.Comment,
                SubmittedAt = feedback.SubmittedAt,
                Items = feedback.FeedbackItems.Select(i => new FeedbackItemDTO
                {
                    OrderItemRef = i.OrderItemRef,
                    Star = i.Star,
                    Comment = i.Comment
                }).ToList()
            };
        }, ct);
    }

    public async Task<FeedbackDTO?> GetByOrder(Guid orderRef, CancellationToken ct)
    {
        if (orderRef == Guid.Empty)
        {
            throw new CustomerException(400, "Invalid order reference.");
        }

        var feedback = await feedbacks.GetFeedbackByOrderAsync(orderRef, ct);
        if (feedback is null)
        {
            return null;
        }

        return MapFeedbackDTO(feedback);
    }

    public async Task<(List<FeedbackDTO> Items, int Total)> GetAll(
        int page,
        int pageSize,
        int? starFilter,
        CancellationToken ct)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100)
        {
            throw new CustomerException(400, "Invalid pagination.");
        }

        var (items, total) = await feedbacks.GetPageAsync(page, pageSize, starFilter, ct);
        return (items.Select(MapFeedbackDTO).ToList(), total);
    }

    public async Task<FeedbackSummaryDTO> GetSummary(CancellationToken ct)
    {
        var (avg, total, s5, s4, s3, s2, s1) = await feedbacks.GetSummaryAsync(ct);
        return new FeedbackSummaryDTO
        {
            AverageRating = avg,
            TotalFeedbacks = total,
            Star5Count = s5,
            Star4Count = s4,
            Star3Count = s3,
            Star2Count = s2,
            Star1Count = s1
        };
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static FeedbackDTO MapFeedbackDTO(Feedback f)
    {
        return new FeedbackDTO
        {
            FeedbackId = f.FeedbackId,
            OrderRef = f.OrderRef,
            CustomerId = f.CustomerId,
            CustomerName = f.Customer?.CustomerName,
            Star = f.Star,
            Comment = f.Comment,
            SubmittedAt = f.SubmittedAt,
            Items = f.FeedbackItems.Select(i => new FeedbackItemDTO
            {
                OrderItemRef = i.OrderItemRef,
                Star = i.Star,
                Comment = i.Comment
            }).ToList()
        };
    }
}

