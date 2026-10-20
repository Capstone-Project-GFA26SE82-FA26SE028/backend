using System.Globalization;
using ApiGateway.DTOs.Restaurant;
using ApiGateway.Swagger;
using Backend.Contracts.Restaurant;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.RestaurantManagement;

[ApiController]
[Route("api/reservations")]
public sealed class ReservationController(ReservationGrpc.ReservationGrpcClient client) : ReservationControllerBase
{
    [HttpPost("{id}/reject")]
    [RequireBearerToken]
    public Task<IActionResult> Reject(Guid id, CancellationToken ct)
    {
        return Execute(async () => Map(await client.RejectAsync(new ReservationIdRequest
        {
            ReservationId = id.ToString()
        }, Options(ct))), ct, "Reservation rejected successfully.");
    }

    [HttpPost("{id}/check-in")]
    [RequireBearerToken]
    public Task<IActionResult> CheckIn(Guid id, CancellationToken ct)
    {
        return Execute(async () => Map(await client.CheckInAsync(new ReservationIdRequest
        {
            ReservationId = id.ToString()
        }, Options(ct))), ct, "Customer arrival recorded successfully.");
    }

    [HttpPost("{id}/cancel")]
    [RequireBearerToken]
    public Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        return Execute(async () => Map(await client.CancelAsync(new ReservationIdRequest
        {
            ReservationId = id.ToString()
        }, Options(ct))), ct, "Reservation cancelled successfully.");
    }

    [HttpPut("{id}")]
    [RequireBearerToken]
    public Task<IActionResult> Update(Guid id, UpdateReservationDto body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var request = new UpdateReservationRequest
            {
                ReservationId = id.ToString(),
                StartAt = body.StartAt ?? string.Empty
            };
            request.TableIds.AddRange((body.TableIds ?? []).Select(value => value.ToString()));
            return Map(await client.UpdateAsync(request, Options(ct)));
        }, ct, "Reservation schedule updated successfully.");
    }

    [HttpPost("{id}/confirm")]
    [RequireBearerToken]
    public Task<IActionResult> Confirm(Guid id, ConfirmReservationDto body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var request = new ConfirmReservationRequest
            {
                ReservationId = id.ToString()
            };
            request.TableIds.AddRange((body.TableIds ?? []).Select(value => value.ToString()));
            return Map(await client.ConfirmAsync(request, Options(ct)));
        }, ct, "Reservation confirmed successfully.");
    }

    [HttpPost]
    public Task<IActionResult> Create(CreateReservationDto body, CancellationToken ct)
    {
        return Execute(async () => Map(await client.CreateAsync(new CreateReservationRequest
        {
            RestaurantId = body.RestaurantId.ToString(),
            RequestedAreaId = body.RequestedAreaId.ToString(),
            ContactName = body.ContactName ?? string.Empty,
            Phone = body.Phone ?? string.Empty,
            PartySize = body.PartySize,
            StartAt = body.StartAt ?? string.Empty
        }, Options(ct))), ct, "Reservation request received and pending review. The restaurant will contact you by phone to confirm availability.", 201);
    }

    [HttpGet]
    [RequireBearerToken]
    public Task<IActionResult> GetAll([FromQuery] ReservationQuery query, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var request = new ReservationListRequest
            {
                UnassignedOnly = query.UnassignedOnly,
                Page = query.Page,
                PageSize = query.PageSize
            };
            if (query.RestaurantId.HasValue)
            {
                request.RestaurantId = query.RestaurantId.Value.ToString();
            }

            if (query.RequestedAreaId.HasValue)
            {
                request.RequestedAreaId = query.RequestedAreaId.Value.ToString();
            }

            if (query.AssignedRestaurantId.HasValue)
            {
                request.AssignedRestaurantId = query.AssignedRestaurantId.Value.ToString();
            }

            if (query.AssignedAreaId.HasValue)
            {
                request.AssignedAreaId = query.AssignedAreaId.Value.ToString();
            }

            if (query.Status is not null)
            {
                request.Status = query.Status;
            }

            if (query.From is not null)
            {
                request.From = query.From;
            }

            if (query.To is not null)
            {
                request.To = query.To;
            }

            var page = await client.GetAllAsync(request, Options(ct));
            return new PageResult<ReservationDto>
            {
                Items = page.Items.Select(Map).ToList(),
                TotalCount = page.TotalCount,
                Page = page.Page,
                PageSize = page.PageSize
            };
        }, ct, "Reservations retrieved successfully.");
    }

    [HttpGet("{id}")]
    [RequireBearerToken]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        return Execute(async () => Map(await client.GetByIdAsync(new ReservationIdRequest
        {
            ReservationId = id.ToString()
        }, Options(ct))), ct, "Reservation retrieved successfully.");
    }

    [HttpGet("availability")]
    [RequireBearerToken]
    public Task<IActionResult> GetAvailability([FromQuery] ReservationAvailabilityQuery query, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.GetAvailabilityAsync(new ReservationAvailabilityRequest
            {
                RestaurantId = query.RestaurantId.ToString(),
                AreaId = query.AreaId.ToString(),
                PartySize = query.PartySize,
                StartAt = query.StartAt ?? string.Empty
            }, Options(ct));
            return new ReservationAvailabilityDto
            {
                RestaurantId = Guid.Parse(reply.RestaurantId),
                AreaId = Guid.Parse(reply.AreaId),
                PartySize = reply.PartySize,
                EvaluatedAt = Timestamp(reply.EvaluatedAt),
                StartAt = Timestamp(reply.StartAt),
                EndAt = Timestamp(reply.EndAt),
                HoldStartsAt = Timestamp(reply.HoldStartsAt),
                HoldUntil = Timestamp(reply.HoldUntil),
                BlockedUntil = Timestamp(reply.BlockedUntil),
                AvailableCapacity = reply.AvailableCapacity,
                HasEnoughCapacity = reply.HasEnoughCapacity,
                RequiresStaffLayoutReview = reply.RequiresStaffLayoutReview,
                Reasons = reply.Reasons.ToArray(),
                Tables = reply.Tables.Select(table => new ReservationTableAvailabilityDto
                {
                    TableId = Guid.Parse(table.TableId),
                    TableCode = table.TableCode,
                    Capacity = table.Capacity,
                    CleaningStatus = table.CleaningStatus,
                    IsScheduleAvailable = table.IsScheduleAvailable,
                    Reasons = table.Reasons.ToArray()
                }).ToArray()
            };
        }, ct, "Reservation availability preview retrieved successfully. This does not hold a table.");
    }

    private static ReservationDto Map(ReservationInfo reservation)
    {
        return new ReservationDto
        {
            ReservationId = Guid.Parse(reservation.ReservationId),
            RestaurantId = Guid.Parse(reservation.RestaurantId),
            RequestedAreaId = Guid.Parse(reservation.RequestedAreaId),
            ContactName = reservation.ContactName,
            Phone = reservation.Phone,
            PartySize = reservation.PartySize,
            StartAt = Timestamp(reservation.StartAt),
            EndAt = Timestamp(reservation.EndAt),
            HoldUntil = reservation.HasHoldUntil ? Timestamp(reservation.HoldUntil) : null,
            Status = reservation.Status,
            ApprovedByRef = reservation.HasApprovedByRef ? Guid.Parse(reservation.ApprovedByRef) : null,
            ConfirmedAt = reservation.HasConfirmedAt ? Timestamp(reservation.ConfirmedAt) : null,
            CancelledAt = reservation.HasCancelledAt ? Timestamp(reservation.CancelledAt) : null,
            CancelledByRef = reservation.HasCancelledByRef ? Guid.Parse(reservation.CancelledByRef) : null,
            AssignedRestaurantId = reservation.HasAssignedRestaurantId ? Guid.Parse(reservation.AssignedRestaurantId) : null,
            AssignedAreaId = reservation.HasAssignedAreaId ? Guid.Parse(reservation.AssignedAreaId) : null,
            HasConsistentAssignment = reservation.HasConsistentAssignment,
            TableSessionId = reservation.HasTableSessionId ? Guid.Parse(reservation.TableSessionId) : null,
            AssignedTableIds = reservation.AssignedTableIds.Select(Guid.Parse).ToArray()
        };
    }

    private static DateTimeOffset Timestamp(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
