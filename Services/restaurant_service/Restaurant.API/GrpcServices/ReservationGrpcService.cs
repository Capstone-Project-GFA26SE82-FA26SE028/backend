using System.Globalization;
using Backend.Contracts.Restaurant;
using Grpc.Core;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;

namespace Restaurant.API.GrpcServices;

public sealed class ReservationGrpcService(IReservationService service) : ReservationGrpc.ReservationGrpcBase
{
    public override async Task<ReservationInfo> Reject(ReservationIdRequest request, ServerCallContext context)
    {
        return Map(await service.Reject(ParseId(request.ReservationId), context.CancellationToken));
    }

    public override async Task<ReservationInfo> CheckIn(ReservationIdRequest request, ServerCallContext context)
    {
        return Map(await service.CheckIn(ParseId(request.ReservationId), context.CancellationToken));
    }

    public override async Task<ReservationInfo> Cancel(ReservationIdRequest request, ServerCallContext context)
    {
        return Map(await service.Cancel(ParseId(request.ReservationId), context.CancellationToken));
    }

    public override async Task<ReservationInfo> Update(UpdateReservationRequest request, ServerCallContext context)
    {
        return Map(await service.Update(ParseId(request.ReservationId), new UpdateReservationDto
        {
            StartAt = request.StartAt,
            TableIds = request.TableIds.Select(ParseId).ToArray()
        }, context.CancellationToken));
    }

    public override async Task<ReservationInfo> Confirm(ConfirmReservationRequest request, ServerCallContext context)
    {
        return Map(await service.Confirm(ParseId(request.ReservationId), new ConfirmReservationDto
        {
            TableIds = request.TableIds.Select(ParseId).ToArray()
        }, context.CancellationToken));
    }

    public override async Task<ReservationInfo> Create(CreateReservationRequest request, ServerCallContext context)
    {
        return Map(await service.Create(new CreateReservationDto
        {
            RestaurantId = ParseId(request.RestaurantId),
            RequestedAreaId = ParseId(request.RequestedAreaId),
            ContactName = request.ContactName,
            Phone = request.Phone,
            PartySize = request.PartySize,
            StartAt = request.StartAt
        }, context.CancellationToken));
    }

    public override async Task<ReservationInfo> GetById(ReservationIdRequest request, ServerCallContext context)
    {
        return Map(await service.GetById(ParseId(request.ReservationId), context.CancellationToken));
    }

    public override async Task<ReservationPage> GetAll(ReservationListRequest request, ServerCallContext context)
    {
        var page = await service.GetAll(new ReservationQuery
        {
            RestaurantId = request.HasRestaurantId ? ParseId(request.RestaurantId) : null,
            RequestedAreaId = request.HasRequestedAreaId ? ParseId(request.RequestedAreaId) : null,
            AssignedRestaurantId = request.HasAssignedRestaurantId ? ParseId(request.AssignedRestaurantId) : null,
            AssignedAreaId = request.HasAssignedAreaId ? ParseId(request.AssignedAreaId) : null,
            UnassignedOnly = request.UnassignedOnly,
            Status = request.HasStatus ? request.Status : null,
            From = request.HasFrom ? request.From : null,
            To = request.HasTo ? request.To : null,
            Page = request.Page,
            PageSize = request.PageSize
        }, context.CancellationToken);
        var reply = new ReservationPage
        {
            TotalCount = page.TotalCount,
            Page = page.Page,
            PageSize = page.PageSize
        };
        reply.Items.AddRange(page.Items.Select(Map));
        return reply;
    }

    public override async Task<ReservationAvailabilityReply> GetAvailability(
        ReservationAvailabilityRequest request, ServerCallContext context)
    {
        var result = await service.GetAvailability(new ReservationAvailabilityQuery
        {
            RestaurantId = ParseId(request.RestaurantId),
            AreaId = ParseId(request.AreaId),
            PartySize = request.PartySize,
            StartAt = request.StartAt
        }, context.CancellationToken);
        var reply = new ReservationAvailabilityReply
        {
            RestaurantId = result.RestaurantId.ToString(),
            AreaId = result.AreaId.ToString(),
            PartySize = result.PartySize,
            EvaluatedAt = result.EvaluatedAt.ToString("O", CultureInfo.InvariantCulture),
            StartAt = result.StartAt.ToString("O", CultureInfo.InvariantCulture),
            EndAt = result.EndAt.ToString("O", CultureInfo.InvariantCulture),
            HoldStartsAt = result.HoldStartsAt.ToString("O", CultureInfo.InvariantCulture),
            HoldUntil = result.HoldUntil.ToString("O", CultureInfo.InvariantCulture),
            BlockedUntil = result.BlockedUntil.ToString("O", CultureInfo.InvariantCulture),
            AvailableCapacity = result.AvailableCapacity,
            HasEnoughCapacity = result.HasEnoughCapacity,
            RequiresStaffLayoutReview = result.RequiresStaffLayoutReview
        };
        reply.Reasons.AddRange(result.Reasons);
        reply.Tables.AddRange(result.Tables.Select(table =>
        {
            var info = new ReservationTableAvailabilityInfo
            {
                TableId = table.TableId.ToString(),
                TableCode = table.TableCode,
                Capacity = table.Capacity,
                CleaningStatus = table.CleaningStatus,
                IsScheduleAvailable = table.IsScheduleAvailable
            };
            info.Reasons.AddRange(table.Reasons);
            return info;
        }));
        return reply;
    }

    private static Guid ParseId(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã tài nguyên không hợp lệ hoặc bị trống."));
        }

        return id;
    }

    private static ReservationInfo Map(ReservationDto reservation)
    {
        var result = new ReservationInfo
        {
            ReservationId = reservation.ReservationId.ToString(),
            RestaurantId = reservation.RestaurantId.ToString(),
            RequestedAreaId = reservation.RequestedAreaId.ToString(),
            ContactName = reservation.ContactName,
            Phone = reservation.Phone,
            PartySize = reservation.PartySize,
            StartAt = reservation.StartAt.ToString("O", CultureInfo.InvariantCulture),
            EndAt = reservation.EndAt.ToString("O", CultureInfo.InvariantCulture),
            Status = reservation.Status,
            HasConsistentAssignment = reservation.HasConsistentAssignment
        };
        if (reservation.HoldUntil.HasValue)
        {
            result.HoldUntil = reservation.HoldUntil.Value.ToString("O", CultureInfo.InvariantCulture);
        }

        if (reservation.ConfirmedAt.HasValue)
        {
            result.ConfirmedAt = reservation.ConfirmedAt.Value.ToString("O", CultureInfo.InvariantCulture);
        }

        if (reservation.CancelledAt.HasValue)
        {
            result.CancelledAt = reservation.CancelledAt.Value.ToString("O", CultureInfo.InvariantCulture);
        }

        if (reservation.CancelledByRef.HasValue)
        {
            result.CancelledByRef = reservation.CancelledByRef.Value.ToString();
        }

        if (reservation.ApprovedByRef.HasValue)
        {
            result.ApprovedByRef = reservation.ApprovedByRef.Value.ToString();
        }

        if (reservation.AssignedRestaurantId.HasValue)
        {
            result.AssignedRestaurantId = reservation.AssignedRestaurantId.Value.ToString();
        }

        if (reservation.AssignedAreaId.HasValue)
        {
            result.AssignedAreaId = reservation.AssignedAreaId.Value.ToString();
        }

        if (reservation.TableSessionId.HasValue)
        {
            result.TableSessionId = reservation.TableSessionId.Value.ToString();
        }

        result.AssignedTableIds.AddRange(reservation.AssignedTableIds.Select(id => id.ToString()));
        return result;
    }
}
