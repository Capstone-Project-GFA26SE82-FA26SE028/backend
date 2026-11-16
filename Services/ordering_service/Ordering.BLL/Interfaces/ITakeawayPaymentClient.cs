using Ordering.Common.DTOs;
using Ordering.DAL.Models;

namespace Ordering.BLL.Interfaces;

public interface ITakeawayPaymentClient
{
    Task<TakeawayPaymentResult> ExecuteAsync(Order order, string action, string reason, string reference, string evidence, CancellationToken ct);
}
