using System.Globalization;
using ApiGateway.Controllers.IdentityController;
using ApiGateway.DTOs;
using ApiGateway.Swagger;
using Backend.Contracts.Billing;
using Backend.Contracts.Identity;
using Backend.Contracts.Ordering;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.DTOs;
using Shared.Common.Pagination;

namespace ApiGateway.Controllers;

[ApiController]
[RequireBearerToken]
public sealed class StaffActivityController(
    AuditLogGrpc.AuditLogGrpcClient auditClient,
    CancellationRequestGrpc.CancellationRequestGrpcClient cancellationClient,
    BillGrpc.BillGrpcClient billClient) : IdentityControllerBase
{
    [HttpGet("/api/restaurants/{restaurantId:guid}/staff-activities")]
    [HttpGet("/api/staff-activities")]
    [ProducesResponseType(typeof(ResponseDTO<PagedResult<StaffActivityDTO>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromRoute] Guid? restaurantId,
        [FromQuery] StaffActivityQueryDTO query,
        CancellationToken ct)
    {
        var targetRestaurantId = restaurantId;
        var activities = new List<StaffActivityDTO>();
        var options = Options(ct);

        // 1. Fetch Audit Logs (Account & user management events)
        try
        {
            var auditRequest = new ActivityListRequest
            {
                Page = 1,
                PageSize = 100
            };

            if (targetRestaurantId.HasValue)
            {
                auditRequest.RestaurantId = targetRestaurantId.Value.ToString();
            }

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                auditRequest.Search = query.Search;
            }

            if (query.From.HasValue)
            {
                auditRequest.FromUtc = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(query.From.Value);
            }

            if (query.To.HasValue)
            {
                auditRequest.ToUtc = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(query.To.Value);
            }

            var auditReply = await auditClient.ListAuditLogsAsync(auditRequest, options);
            if (auditReply.IsSuccess && auditReply.Items.Count > 0)
            {
                foreach (var log in auditReply.Items)
                {
                    activities.Add(new StaffActivityDTO
                    {
                        ActivityId = Guid.Parse(log.AuditLogId),
                        RestaurantId = targetRestaurantId ?? Guid.Empty,
                        Timestamp = log.CreatedAt.ToDateTime(),
                        ActivityType = "AccountAudit",
                        StaffId = log.HasUserId ? Guid.Parse(log.UserId) : null,
                        StaffName = log.HasFullName ? log.FullName : log.UserName,
                        StaffRole = "Staff",
                        Target = log.HasEntityName ? $"{log.EntityName} ({log.EntityId})" : "Tài khoản",
                        ActionDescription = log.HasAction ? log.Action : "Hoạt động tài khoản",
                        Reason = null,
                        Metadata = new Dictionary<string, string>
                        {
                            ["ipAddress"] = log.HasIpAddress ? log.IpAddress : "",
                            ["oldValue"] = log.HasOldValue ? log.OldValue : "",
                            ["newValue"] = log.HasNewValue ? log.NewValue : ""
                        }
                    });
                }
            }
        }
        catch (RpcException)
        {
            // Identity service might be unreachable in tests or not have permission
        }

        // 2. Fetch Item Cancellation Requests
        try
        {
            var cancelReply = await cancellationClient.GetAllAsync(new CancellationListRequest
            {
                Access = new OrderingAccess(),
                Status = "",
                Page = 1,
                PageSize = 100
            }, options);

            if (cancelReply.Items.Count > 0)
            {
                foreach (var item in cancelReply.Items)
                {
                    var itemTime = DateTime.TryParse(item.RequestedAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsedTime)
                        ? parsedTime
                        : DateTime.UtcNow;

                    activities.Add(new StaffActivityDTO
                    {
                        ActivityId = Guid.Parse(item.CancellationRequestId),
                        RestaurantId = targetRestaurantId ?? Guid.Empty,
                        Timestamp = itemTime,
                        ActivityType = "ItemCancellation",
                        StaffId = Guid.TryParse(item.RequestedByRef, out var reqId) ? reqId : null,
                        StaffName = !string.IsNullOrWhiteSpace(item.RequestedByRef) ? $"Nhân viên {item.RequestedByRef[..8]}" : "Nhân viên",
                        StaffRole = "Waiter",
                        Target = $"Món {item.ItemName} (x{item.Quantity})",
                        ActionDescription = $"Yêu cầu hủy món - Trạng thái: {item.Status}",
                        Reason = item.Reason,
                        Metadata = new Dictionary<string, string>
                        {
                            ["orderId"] = item.OrderId,
                            ["orderNumber"] = item.OrderNumber,
                            ["tableSessionId"] = item.TableSessionId,
                            ["status"] = item.Status,
                            ["decisionReason"] = item.HasDecisionReason ? item.DecisionReason : ""
                        }
                    });
                }
            }
        }
        catch (RpcException)
        {
            // Ordering service cancellation may not have records
        }

        // 3. Fetch Bill Adjustments (Voucher & Manual Discounts)
        try
        {
            var billReply = await billClient.GetAllAsync(new BillListRequest
            {
                RestaurantId = targetRestaurantId?.ToString(),
                Page = 1,
                PageSize = 50
            }, options);

            if (billReply.Items.Count > 0)
            {
                foreach (var bill in billReply.Items)
                {
                    if (bill.Adjustments.Count > 0)
                    {
                        foreach (var adj in bill.Adjustments)
                        {
                            activities.Add(new StaffActivityDTO
                            {
                                ActivityId = Guid.Parse(adj.BillAdjustmentId),
                                RestaurantId = Guid.Parse(bill.RestaurantId),
                                Timestamp = DateTime.UtcNow,
                                ActivityType = "BillAdjustment",
                                StaffId = adj.HasActorUserRef ? Guid.Parse(adj.ActorUserRef) : null,
                                StaffName = "Thu ngân / Quản lý",
                                StaffRole = "Cashier",
                                Target = $"Hóa đơn {bill.BillNumber}",
                                ActionDescription = $"Giảm giá {adj.Amount} đ ({adj.Type})",
                                Reason = adj.HasReason ? adj.Reason : $"Áp dụng {adj.Type}",
                                Metadata = new Dictionary<string, string>
                                {
                                    ["billId"] = bill.BillId,
                                    ["billNumber"] = bill.BillNumber,
                                    ["adjustmentType"] = adj.Type,
                                    ["amount"] = adj.Amount,
                                    ["ruleSnapshot"] = adj.RuleSnapshot
                                }
                            });
                        }
                    }
                }
            }
        }
        catch (RpcException)
        {
            // Billing service might not have bills
        }

        // Filter by ActivityType if specified
        if (!string.IsNullOrWhiteSpace(query.ActivityType) && !query.ActivityType.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            activities = activities
                .Where(a => a.ActivityType.Equals(query.ActivityType, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Filter by Search if specified
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            activities = activities
                .Where(a => (a.StaffName != null && a.StaffName.ToLowerInvariant().Contains(search))
                    || a.Target.ToLowerInvariant().Contains(search)
                    || (a.Reason != null && a.Reason.ToLowerInvariant().Contains(search))
                    || a.ActionDescription.ToLowerInvariant().Contains(search))
                .ToList();
        }

        // Filter by Date range
        if (query.From.HasValue)
        {
            activities = activities.Where(a => a.Timestamp >= query.From.Value.UtcDateTime).ToList();
        }

        if (query.To.HasValue)
        {
            activities = activities.Where(a => a.Timestamp <= query.To.Value.UtcDateTime).ToList();
        }

        // Sort descending by timestamp
        var sorted = activities.OrderByDescending(a => a.Timestamp).ToList();
        var totalCount = sorted.Count;
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 20 : query.PageSize;

        var pagedItems = sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var result = new PagedResult<StaffActivityDTO>(pagedItems, totalCount, page, pageSize);
        return Ok(new ResponseDTO("Thành công.", 200, true, result));
    }
}
