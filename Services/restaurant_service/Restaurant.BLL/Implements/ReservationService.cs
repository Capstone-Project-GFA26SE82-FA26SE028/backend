using Microsoft.Extensions.Options;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;
using Restaurant.Common.Reservations;
using Restaurant.Common.Settings;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;

namespace Restaurant.BLL.Implements;

public sealed class ReservationService(
    IReservationRepository reservations,
    IRestaurantRepository restaurants,
    IAreaRepository areas,
    ITableRepository tables,
    ISessionTableRepository sessionTables,
    IReservationTransaction transaction,
    IReservationAccess access,
    IOptions<ReservationSettings> settings,
    TimeProvider clock) : IReservationService
{
    public async Task<ReservationDto> Reject(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            throw new ManagementException(400, "Mã đặt bàn không hợp lệ hoặc bị trống.");
        }

        ReservationRules.Configuration(settings.Value);
        var result = await transaction.ExecuteAsync(async () =>
        {
            var reservation = await reservations.LockAsync(id, ct)
                ?? throw new ManagementException(404, "Không tìm thấy thông tin đặt bàn.");
            var actorId = await access.RequireMutationAsync(reservation.RestaurantId, ct);
            if (reservation.Status != ReservationStatuses.Pending)
            {
                throw new ManagementException(409, "Chỉ có thể từ chối đặt bàn đang ở trạng thái Chờ xử lý (Pending).");
            }

            if (clock.GetUtcNow().UtcDateTime > reservation.StartAt.AddMinutes(-settings.Value.MinimumAdvanceMinutes))
            {
                reservation.Status = ReservationStatuses.Expired;
                return (ReservationDto?)null;
            }

            reservation.Status = ReservationStatuses.Rejected;
            reservation.ApprovedByRef = actorId;
            return Map(reservation);
        }, ct);
        return result ?? throw new ManagementException(409, "Đã quá hạn duyệt đặt bàn. Yêu cầu đã hết hạn (Expired).");
    }

    public async Task<ReservationDto> Create(CreateReservationDto request, CancellationToken ct)
    {
        if (request is null || request.RestaurantId == Guid.Empty || request.RequestedAreaId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.ContactName) || request.ContactName.Trim().Length > 255
            || request.ContactName.Any(char.IsControl) || string.IsNullOrWhiteSpace(request.Phone)
            || request.Phone.Trim().Length > 20 || !ValidPhone(request.Phone) || request.PartySize <= 0)
        {
            throw new ManagementException(400, "Vui lòng cung cấp nhà hàng, khu vực, tên người liên hệ tối đa 255 ký tự, số điện thoại tối đa 20 ký tự và số lượng khách hợp lệ.");
        }

        var now = reservations.NormalizeTimestamp(clock.GetUtcNow().UtcDateTime);
        var window = ReservationRules.Window(request.RestaurantId, request.StartAt, now, settings.Value, reservations);
        return await transaction.ExecuteAsync(async () =>
        {
            var restaurant = await restaurants.GetByIdAsync(request.RestaurantId, ct)
                ?? throw new ManagementException(404, "Không tìm thấy nhà hàng.");
            var area = await areas.GetByIdAsync(request.RequestedAreaId, ct)
                ?? throw new ManagementException(404, "Không tìm thấy khu vực.");
            if (area.RestaurantId != request.RestaurantId || !area.IsActive)
            {
                throw new ManagementException(409, "Khu vực yêu cầu đang ngừng hoạt động hoặc không thuộc nhà hàng đã chọn.");
            }

            if (!WithinOpeningHours(restaurant, window))
            {
                throw new ManagementException(409, "Khoảng thời gian yêu cầu nằm ngoài giờ mở cửa của nhà hàng.");
            }

            var reservation = new Reservation
            {
                ReservationId = Guid.NewGuid(),
                RestaurantId = request.RestaurantId,
                RequestedAreaId = request.RequestedAreaId,
                ContactName = request.ContactName.Trim(),
                Phone = request.Phone.Trim(),
                PartySize = request.PartySize,
                StartAt = window.StartAt,
                EndAt = window.EndAt,
                HoldUntil = window.HoldUntil,
                Status = ReservationStatuses.Pending
            };
            await reservations.AddAsync(reservation, ct);
            return Map(reservation);
        }, ct);
    }

    public async Task<ReservationDto> Confirm(Guid id, ConfirmReservationDto request, CancellationToken ct)
    {
        if (id == Guid.Empty || request is null || request.TableIds is null || request.TableIds.Length == 0
            || request.TableIds.Any(tableId => tableId == Guid.Empty)
            || request.TableIds.Distinct().Count() != request.TableIds.Length)
        {
            throw new ManagementException(400, "Vui lòng cung cấp mã đặt bàn hợp lệ và danh sách mã bàn không trùng lặp.");
        }

        ReservationRules.Configuration(settings.Value);
        var result = await transaction.ExecuteAsync(async () =>
        {
            var reservation = await reservations.LockAsync(id, ct)
                ?? throw new ManagementException(404, "Không tìm thấy thông tin đặt bàn.");
            var actorId = await access.RequireMutationAsync(reservation.RestaurantId, ct);
            if (reservation.Status != ReservationStatuses.Pending || reservation.TableSession is not null
                || reservation.Tables.Count != 0)
            {
                throw new ManagementException(409, "Chỉ có thể xác nhận đặt bàn đang ở trạng thái Chờ xử lý (Pending).");
            }

            var now = clock.GetUtcNow().UtcDateTime;
            if (now > reservation.StartAt.AddMinutes(-settings.Value.MinimumAdvanceMinutes))
            {
                reservation.Status = ReservationStatuses.Expired;
                return (ReservationDto?)null;
            }

            if (reservation.EndAt <= reservation.StartAt)
            {
                throw new ManagementException(409, "Thời gian đặt bàn dự kiến không hợp lệ.");
            }

            var window = StoredWindow(reservation);
            var selected = await PrepareScheduleAsync(reservation, request.TableIds, window, ct);

            var confirmedAt = clock.GetUtcNow().UtcDateTime;
            if (confirmedAt > reservation.StartAt.AddMinutes(-settings.Value.MinimumAdvanceMinutes))
            {
                reservation.Status = ReservationStatuses.Expired;
                return (ReservationDto?)null;
            }

            foreach (var table in selected)
            {
                reservation.Tables.Add(table);
            }

            reservation.Status = ReservationStatuses.Confirmed;
            reservation.ConfirmedAt = reservations.NormalizeTimestamp(confirmedAt);
            reservation.ApprovedByRef = actorId;
            reservation.HoldUntil = window.HoldUntil;
            return Map(reservation);
        }, ct);
        return result ?? throw new ManagementException(409, "Đã quá hạn duyệt đặt bàn. Yêu cầu đã hết hạn (Expired).");
    }

    public async Task<ReservationDto> CheckIn(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            throw new ManagementException(400, "Mã đặt bàn không hợp lệ hoặc bị trống.");
        }

        ReservationRules.Configuration(settings.Value);
        var result = await transaction.ExecuteAsync(async () =>
        {
            var reservation = await reservations.LockAsync(id, ct)
                ?? throw new ManagementException(404, "Không tìm thấy thông tin đặt bàn.");
            await access.RequireMutationAsync(reservation.RestaurantId, ct);
            RequireConfirmed(reservation);
            var window = StoredWindow(reservation);
            var now = clock.GetUtcNow().UtcDateTime;
            if (now >= window.HoldUntil)
            {
                reservation.Status = ReservationStatuses.NoShow;
                return (ReservationDto?)null;
            }

            if (now < window.HoldStartsAt)
            {
                throw new ManagementException(409, "Chỉ có thể nhận bàn trước giờ bắt đầu tối đa 30 phút.");
            }

            if (reservation.Tables.Count == 0)
            {
                throw new ManagementException(409, "Đặt bàn chưa được chỉ định bàn nào.");
            }

            long capacity = 0;
            foreach (var tableId in reservation.Tables.Select(table => table.TableId).Order())
            {
                var table = await tables.LockForReservationAsync(tableId, ct)
                    ?? throw new ManagementException(409, "Bàn được chỉ định không còn tồn tại.");
                if (!table.IsActive || !table.Area.IsActive || table.Capacity <= 0
                    || table.AreaId != reservation.RequestedAreaId || table.Area.RestaurantId != reservation.RestaurantId)
                {
                    throw new ManagementException(409, "Bàn được chỉ định không còn phù hợp với khu vực, nhà hàng hoặc trạng thái hoạt động.");
                }

                capacity += table.Capacity;
            }

            if (capacity < reservation.PartySize)
            {
                throw new ManagementException(409, "Các bàn được chỉ định không đủ sức chứa cho số lượng khách.");
            }

            if (clock.GetUtcNow().UtcDateTime >= window.HoldUntil)
            {
                reservation.Status = ReservationStatuses.NoShow;
                return (ReservationDto?)null;
            }

            reservation.Status = ReservationStatuses.CheckedIn;
            return Map(reservation);
        }, ct);
        return result ?? throw new ManagementException(409, "Đã quá giờ nhận bàn. Đặt bàn được đánh dấu là Vắng mặt (NoShow).");
    }

    public async Task<ReservationDto> Cancel(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            throw new ManagementException(400, "Mã đặt bàn không hợp lệ hoặc bị trống.");
        }

        ReservationRules.Configuration(settings.Value);
        return await transaction.ExecuteAsync(async () =>
        {
            var reservation = await reservations.LockAsync(id, ct)
                ?? throw new ManagementException(404, "Không tìm thấy thông tin đặt bàn.");
            var actorId = await access.RequireMutationAsync(reservation.RestaurantId, ct);
            RequireConfirmed(reservation);
            RequireChangeDeadline(reservation);
            reservation.Status = ReservationStatuses.Cancelled;
            reservation.CancelledAt = reservations.NormalizeTimestamp(clock.GetUtcNow().UtcDateTime);
            reservation.CancelledByRef = actorId;
            return Map(reservation);
        }, ct);
    }

    public async Task<ReservationDto> Update(Guid id, UpdateReservationDto request, CancellationToken ct)
    {
        ValidateTableRequest(id, request?.TableIds);
        ReservationRules.Configuration(settings.Value);
        return await transaction.ExecuteAsync(async () =>
        {
            var reservation = await reservations.LockAsync(id, ct)
                ?? throw new ManagementException(404, "Không tìm thấy thông tin đặt bàn.");
            await access.RequireMutationAsync(reservation.RestaurantId, ct);
            RequireConfirmed(reservation);
            RequireChangeDeadline(reservation);
            var window = ReservationRules.Window(reservation.RestaurantId, request!.StartAt,
                clock.GetUtcNow().UtcDateTime, settings.Value, reservations);
            var selected = await PrepareScheduleAsync(reservation, request.TableIds, window, ct);
            RequireChangeDeadline(reservation);
            window = ReservationRules.Window(reservation.RestaurantId, request.StartAt,
                clock.GetUtcNow().UtcDateTime, settings.Value, reservations);
            reservation.StartAt = window.StartAt;
            reservation.EndAt = window.EndAt;
            reservation.HoldUntil = window.HoldUntil;
            reservation.Tables.Clear();
            foreach (var table in selected)
            {
                reservation.Tables.Add(table);
            }

            return Map(reservation);
        }, ct);
    }

    public async Task<PageResult<ReservationDto>> GetAll(ReservationQuery query, CancellationToken ct)
    {
        var scope = await access.GetReadScopeAsync(ct);
        var search = Search(query);
        search.AllowedRestaurantIds = scope;
        RequireScope(scope, query.RestaurantId);
        RequireScope(scope, query.AssignedRestaurantId);
        if (query.RestaurantId.HasValue && await restaurants.GetByIdAsync(query.RestaurantId.Value, ct) is null)
        {
            throw new ManagementException(404, "Không tìm thấy nhà hàng.");
        }

        if (query.RequestedAreaId.HasValue)
        {
            var requestedArea = await areas.GetByIdAsync(query.RequestedAreaId.Value, ct)
                ?? throw new ManagementException(404, "Không tìm thấy khu vực.");
            RequireScope(scope, requestedArea.RestaurantId);
            if (query.RestaurantId.HasValue && requestedArea.RestaurantId != query.RestaurantId.Value)
            {
                throw new ManagementException(400, "Khu vực yêu cầu không thuộc nhà hàng đã chọn.");
            }
        }

        if (query.AssignedRestaurantId.HasValue && await restaurants.GetByIdAsync(query.AssignedRestaurantId.Value, ct) is null)
        {
            throw new ManagementException(404, "Không tìm thấy nhà hàng.");
        }

        if (query.AssignedAreaId.HasValue)
        {
            var area = await areas.GetByIdAsync(query.AssignedAreaId.Value, ct)
                ?? throw new ManagementException(404, "Không tìm thấy khu vực.");
            RequireScope(scope, area.RestaurantId);
            if (query.AssignedRestaurantId.HasValue && area.RestaurantId != query.AssignedRestaurantId.Value)
            {
                throw new ManagementException(400, "Khu vực chỉ định không thuộc nhà hàng đã chọn.");
            }
        }

        var page = await reservations.GetPageAsync(search, ct);
        return new PageResult<ReservationDto>
        {
            Items = page.Items.Select(Map).ToList(),
            TotalCount = page.TotalCount,
            Page = page.Page,
            PageSize = page.PageSize
        };
    }

    public async Task<ReservationDto> GetById(Guid id, CancellationToken ct)
    {
        var scope = await access.GetReadScopeAsync(ct);
        if (id == Guid.Empty)
        {
            throw new ManagementException(400, "Mã đặt bàn không hợp lệ hoặc bị trống.");
        }

        var reservation = await reservations.GetByIdAsync(id, ct)
            ?? throw new ManagementException(404, "Không tìm thấy thông tin đặt bàn.");
        RequireScope(scope, reservation.RestaurantId);
        return Map(reservation);
    }

    public async Task<ReservationAvailabilityDto> GetAvailability(ReservationAvailabilityQuery query, CancellationToken ct)
    {
        await access.RequireReadAsync(query?.RestaurantId ?? Guid.Empty, ct);
        if (query is null || query.RestaurantId == Guid.Empty || query.AreaId == Guid.Empty || query.PartySize <= 0)
        {
            throw new ManagementException(400, "Yêu cầu thông tin nhà hàng, khu vực và số lượng khách lớn hơn 0.");
        }

        var now = reservations.NormalizeTimestamp(clock.GetUtcNow().UtcDateTime);
        var window = ReservationRules.Window(query.RestaurantId, query.StartAt, now, settings.Value, reservations);
        var restaurant = await restaurants.GetByIdAsync(query.RestaurantId, ct)
            ?? throw new ManagementException(404, "Không tìm thấy nhà hàng.");
        var area = await areas.GetByIdAsync(query.AreaId, ct)
            ?? throw new ManagementException(404, "Không tìm thấy khu vực.");
        if (area.RestaurantId != query.RestaurantId)
        {
            throw new ManagementException(400, "Khu vực không thuộc nhà hàng đã chọn.");
        }

        var globalReasons = new List<string>();
        if (!area.IsActive)
        {
            globalReasons.Add("Khu vực đã ngừng hoạt động.");
        }

        if (!WithinOpeningHours(restaurant, window))
        {
            globalReasons.Add("Khoảng thời gian yêu cầu nằm ngoài giờ mở cửa của nhà hàng.");
        }

        var candidates = await tables.GetForReservationAreaAsync(query.AreaId, ct);
        var ids = candidates.Select(table => table.TableId).ToArray();
        var scheduled = await reservations.GetScheduledForTablesAsync(ids, null, ct);
        var occupied = await sessionTables.GetUnreleasedForTablesAsync(ids, ct);
        var result = new List<ReservationTableAvailabilityDto>();
        foreach (var table in candidates)
        {
            var reasons = new List<string>(globalReasons);
            if (!table.IsActive || table.Capacity <= 0)
            {
                reasons.Add("Bàn đã ngừng hoạt động hoặc có sức chứa không hợp lệ.");
            }

            if (window.HoldStartsAt <= now && occupied.Any(link => link.TableId == table.TableId))
            {
                reasons.Add("Bàn đang được gán cho một phiên chưa giải phóng; không thể đảm bảo thời điểm bàn trống.");
            }

            foreach (var reservation in scheduled.Where(reservation => reservation.Tables.Any(assigned => assigned.TableId == table.TableId)))
            {
                if (reservation.EndAt <= reservation.StartAt
                    || (reservation.HoldUntil.HasValue && reservation.HoldUntil.Value < reservation.StartAt))
                {
                    reasons.Add("Một yêu cầu đặt bàn đã xác nhận có khoảng thời gian không hợp lệ và cần nhân viên kiểm tra.");
                    continue;
                }

                DateTime start;
                try
                {
                    start = reservation.StartAt.AddMinutes(-settings.Value.HoldBeforeMinutes);
                }
                catch (ArgumentOutOfRangeException)
                {
                    reasons.Add("Một yêu cầu đặt bàn đã xác nhận có khoảng thời gian giữ bàn không hợp lệ.");
                    continue;
                }

                var end = reservation.HoldUntil.HasValue && reservation.HoldUntil.Value > reservation.EndAt
                    ? reservation.HoldUntil.Value
                    : reservation.EndAt;
                if (start < window.BlockedUntil && window.HoldStartsAt < end)
                {
                    reasons.Add("Bàn bị trùng với một yêu cầu đặt bàn đã xác nhận, bao gồm cả khoảng thời gian giữ bàn.");
                }
            }

            result.Add(new ReservationTableAvailabilityDto
            {
                TableId = table.TableId,
                TableCode = table.TableCode,
                Capacity = table.Capacity,
                CleaningStatus = table.CleaningStatus,
                IsScheduleAvailable = reasons.Count == 0,
                Reasons = reasons.Distinct().ToArray()
            });
        }

        var capacity = result.Where(table => table.IsScheduleAvailable).Sum(table => (long)table.Capacity);
        return new ReservationAvailabilityDto
        {
            RestaurantId = query.RestaurantId,
            AreaId = query.AreaId,
            PartySize = query.PartySize,
            EvaluatedAt = ReservationRules.Utc(now),
            StartAt = ReservationRules.Utc(window.StartAt),
            EndAt = ReservationRules.Utc(window.EndAt),
            HoldStartsAt = ReservationRules.Utc(window.HoldStartsAt),
            HoldUntil = ReservationRules.Utc(window.HoldUntil),
            BlockedUntil = ReservationRules.Utc(window.BlockedUntil),
            AvailableCapacity = (int)Math.Min(capacity, int.MaxValue),
            HasEnoughCapacity = capacity >= query.PartySize,
            Reasons = globalReasons,
            Tables = result
        };
    }

    private async Task<List<Table>> PrepareScheduleAsync(Reservation reservation, IReadOnlyCollection<Guid> tableIds,
        ReservationWindowDto window, CancellationToken ct)
    {
        var restaurant = await restaurants.GetByIdAsync(reservation.RestaurantId, ct)
            ?? throw new ManagementException(404, "Không tìm thấy nhà hàng.");
        var area = await areas.GetByIdAsync(reservation.RequestedAreaId, ct)
            ?? throw new ManagementException(404, "Không tìm thấy khu vực.");
        if (!area.IsActive || area.RestaurantId != reservation.RestaurantId || !WithinOpeningHours(restaurant, window))
        {
            throw new ManagementException(409, "Khu vực yêu cầu hoặc giờ mở cửa không còn hỗ trợ đặt bàn này.");
        }

        var locked = new Dictionary<Guid, Table>();
        foreach (var tableId in reservation.Tables.Select(table => table.TableId).Concat(tableIds).Distinct().Order())
        {
            var table = await tables.LockForReservationAsync(tableId, ct)
                ?? throw new ManagementException(404, "Không tìm thấy bàn.");
            locked.Add(tableId, table);
        }

        var selected = tableIds.Select(tableId => locked[tableId]).ToList();
        if (selected.Any(table => !table.IsActive || table.Capacity <= 0 || table.AreaId != area.AreaId
            || table.Area.RestaurantId != restaurant.RestaurantId))
        {
            throw new ManagementException(409, "Các bàn đã chọn phải đang hoạt động và thuộc khu vực, nhà hàng đã chọn.");
        }

        if (selected.Sum(table => (long)table.Capacity) < reservation.PartySize)
        {
            throw new ManagementException(409, "Các bàn đã chọn không đủ sức chứa cho số lượng khách.");
        }

        var scheduled = await reservations.GetScheduledForTablesAsync(tableIds, reservation.ReservationId, ct);
        foreach (var existing in scheduled)
        {
            var existingEnd = existing.HoldUntil.HasValue && existing.HoldUntil.Value > existing.EndAt
                ? existing.HoldUntil.Value
                : existing.EndAt;
            if (existing.EndAt <= existing.StartAt
                || (existing.StartAt.AddMinutes(-settings.Value.HoldBeforeMinutes) < window.BlockedUntil
                    && window.HoldStartsAt < existingEnd))
            {
                throw new ManagementException(409, "Các bàn đã chọn bị trùng lịch với một đặt bàn đã xác nhận khác (bao gồm khoảng thời gian giữ bàn).");
            }
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (window.HoldStartsAt <= now)
        {
            var occupied = await sessionTables.GetUnreleasedForTablesAsync(tableIds, ct);
            if (occupied.Any() || selected.Any(table => table.CleaningStatus != "Clean"))
            {
                throw new ManagementException(409, "Các bàn đã chọn phải trống và sẵn sàng khi bắt đầu thời gian giữ bàn.");
            }
        }

        return selected;
    }

    private ReservationWindowDto StoredWindow(Reservation reservation)
    {
        var holdUntil = reservation.HoldUntil
            ?? throw new ManagementException(409, "Thiếu thời hạn giữ bàn.");
        if (reservation.EndAt <= reservation.StartAt || holdUntil < reservation.StartAt)
        {
            throw new ManagementException(409, "Thời gian đặt bàn hoặc thời gian giữ bàn không hợp lệ.");
        }

        return new ReservationWindowDto
        {
            StartAt = reservation.StartAt,
            EndAt = reservation.EndAt,
            HoldStartsAt = reservation.StartAt.AddMinutes(-settings.Value.HoldBeforeMinutes),
            HoldUntil = holdUntil,
            BlockedUntil = holdUntil > reservation.EndAt ? holdUntil : reservation.EndAt
        };
    }

    private static void ValidateTableRequest(Guid id, IReadOnlyCollection<Guid>? tableIds)
    {
        if (id == Guid.Empty || tableIds is null || tableIds.Count == 0 || tableIds.Any(value => value == Guid.Empty)
            || tableIds.Distinct().Count() != tableIds.Count)
        {
            throw new ManagementException(400, "Vui lòng cung cấp mã đặt bàn hợp lệ và danh sách mã bàn không trùng lặp.");
        }
    }

    private static void RequireConfirmed(Reservation reservation)
    {
        if (reservation.Status != ReservationStatuses.Confirmed)
        {
            throw new ManagementException(409, "Chỉ có thể chỉnh sửa đặt bàn khi đang ở trạng thái Đã xác nhận (Confirmed).");
        }
    }

    private void RequireChangeDeadline(Reservation reservation)
    {
        if (clock.GetUtcNow().UtcDateTime > reservation.StartAt.AddMinutes(-settings.Value.MinimumAdvanceMinutes))
        {
            throw new ManagementException(409, "Chỉ có thể thay đổi đặt bàn trước giờ bắt đầu ít nhất 30 phút.");
        }
    }

    private ReservationSearch Search(ReservationQuery query)
    {
        if (query is null || query.Page < 1 || query.PageSize is < 1 or > 100
            || (long)(query.Page - 1) * query.PageSize > int.MaxValue
            || query.AssignedRestaurantId == Guid.Empty || query.AssignedAreaId == Guid.Empty
            || query.RestaurantId == Guid.Empty || query.RequestedAreaId == Guid.Empty
            || (query.UnassignedOnly && (query.AssignedRestaurantId.HasValue || query.AssignedAreaId.HasValue))
            || (query.Status is not null && !ReservationStatuses.IsKnown(query.Status)))
        {
            throw new ManagementException(400, "Phân trang, bộ lọc địa điểm hoặc trạng thái đặt bàn không hợp lệ.");
        }

        DateTime? from = null;
        DateTime? to = null;
        if (query.From is not null)
        {
            if (!ReservationRules.TryUtc(query.From, out var parsed))
            {
                throw new ManagementException(400, "From phải là mốc thời gian có độ lệch múi giờ UTC hợp lệ.");
            }

            from = reservations.NormalizeTimestamp(parsed);
        }

        if (query.To is not null)
        {
            if (!ReservationRules.TryUtc(query.To, out var parsed))
            {
                throw new ManagementException(400, "To phải là mốc thời gian có độ lệch múi giờ UTC hợp lệ.");
            }

            to = reservations.NormalizeTimestamp(parsed);
        }

        if (from.HasValue && to.HasValue && from >= to)
        {
            throw new ManagementException(400, "Thời gian To phải sau thời gian From.");
        }

        return new ReservationSearch
        {
            RestaurantId = query.RestaurantId,
            RequestedAreaId = query.RequestedAreaId,
            AssignedRestaurantId = query.AssignedRestaurantId,
            AssignedAreaId = query.AssignedAreaId,
            UnassignedOnly = query.UnassignedOnly,
            Status = query.Status,
            From = from,
            To = to,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    private static void RequireScope(IReadOnlyList<Guid>? scope, Guid? restaurantId)
    {
        if (scope is not null && restaurantId.HasValue && !scope.Contains(restaurantId.Value))
        {
            throw new ManagementException(403, "Bạn không được phân công vào nhà hàng này.");
        }
    }

    private static bool ValidPhone(string phone)
    {
        var value = phone.Trim();
        var digits = value.StartsWith('+') ? value[1..] : value;
        return digits.Length > 0 && digits.All(char.IsAsciiDigit);
    }

    private static bool WithinOpeningHours(global::Restaurant.DAL.Models.Restaurant restaurant, ReservationWindowDto window)
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(restaurant.TimeZone);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            throw new ManagementException(503, "Múi giờ kinh doanh của nhà hàng chưa được cấu hình chính xác.");
        }

        var start = TimeZoneInfo.ConvertTimeFromUtc(window.StartAt, zone);
        var end = TimeZoneInfo.ConvertTimeFromUtc(window.EndAt, zone);
        return start.Date == end.Date && restaurant.OpeningHours.Any(hour => hour.DayOfWeek == (int)start.DayOfWeek
            && hour.OpenAt < hour.CloseAt && TimeOnly.FromDateTime(start) >= hour.OpenAt
            && TimeOnly.FromDateTime(end) <= hour.CloseAt);
    }

    private static ReservationDto Map(Reservation reservation)
    {
        var assigned = reservation.Tables.ToArray();
        var restaurantIds = assigned.Select(table => table.Area.RestaurantId).Distinct().ToArray();
        var areaIds = assigned.Select(table => table.AreaId).Distinct().ToArray();
        return new ReservationDto
        {
            ReservationId = reservation.ReservationId,
            RestaurantId = reservation.RestaurantId,
            RequestedAreaId = reservation.RequestedAreaId,
            ContactName = reservation.ContactName,
            Phone = reservation.Phone,
            PartySize = reservation.PartySize,
            StartAt = ReservationRules.Utc(reservation.StartAt),
            EndAt = ReservationRules.Utc(reservation.EndAt),
            HoldUntil = reservation.HoldUntil.HasValue ? ReservationRules.Utc(reservation.HoldUntil.Value) : null,
            Status = reservation.Status,
            ApprovedByRef = reservation.ApprovedByRef,
            ConfirmedAt = reservation.ConfirmedAt.HasValue ? ReservationRules.Utc(reservation.ConfirmedAt.Value) : null,
            CancelledAt = reservation.CancelledAt.HasValue ? ReservationRules.Utc(reservation.CancelledAt.Value) : null,
            CancelledByRef = reservation.CancelledByRef,
            AssignedRestaurantId = restaurantIds.Length == 1 ? restaurantIds[0] : null,
            AssignedAreaId = areaIds.Length == 1 ? areaIds[0] : null,
            HasConsistentAssignment = assigned.Length > 0 && restaurantIds.Length == 1 && areaIds.Length == 1,
            TableSessionId = reservation.TableSession?.TableSessionId,
            AssignedTableIds = assigned.Select(table => table.TableId).Order().ToArray()
        };
    }
}
