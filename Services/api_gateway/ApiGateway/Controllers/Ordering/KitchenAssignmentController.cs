using ApiGateway.DTOs.Ordering;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Ordering;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ApiGateway.Controllers.Ordering;

[ApiController]
[Route("api/kitchen-assignments")]
[RequireBearerToken]
public sealed class KitchenAssignmentController(KitchenAssignmentGrpc.KitchenAssignmentGrpcClient client,
    IOptions<OrderingClientSettings> settings) : OrderingControllerBase(settings)
{
    [HttpGet]
    public Task<IActionResult> GetAll(CancellationToken ct, int page = 1, int pageSize = 20)
    {
        return Execute(async () =>
        {
            var reply = await client.GetAllAsync(new KitchenAssignmentListRequest
            {
                Page = page,
                PageSize = pageSize
            }, Options(ct));
            return Result(new
            {
                Items = reply.Items.Select(Map).ToArray(),
                reply.TotalCount,
                reply.Page,
                reply.PageSize
            });
        }, ct);
    }

    [HttpGet("staff")]
    public Task<IActionResult> GetEmployees(CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.GetEmployeesAsync(new Empty(), Options(ct));
            return Result(reply.Items.Select(Map).ToArray());
        }, ct);
    }

    [HttpGet("/api/kitchen-stations/{stationId:guid}/assignment")]
    public Task<IActionResult> GetById(Guid stationId, CancellationToken ct)
    {
        return Execute(async () =>
        {
            return Result(Map(await client.GetByIdAsync(new KitchenAssignmentRequest
            {
                StationId = stationId.ToString()
            }, Options(ct))));
        }, ct);
    }

    [HttpPost("/api/kitchen-stations/{stationId:guid}/assignment")]
    public Task<IActionResult> Create(Guid stationId, KitchenAssignmentWriteDTO body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            return Result(Map(await client.CreateAsync(Write(stationId, body), Options(ct))), 201);
        }, ct);
    }

    [HttpPut("/api/kitchen-stations/{stationId:guid}/assignment")]
    public Task<IActionResult> Update(Guid stationId, KitchenAssignmentWriteDTO body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            return Result(Map(await client.UpdateAsync(Write(stationId, body), Options(ct))));
        }, ct);
    }

    [HttpDelete("/api/kitchen-stations/{stationId:guid}/assignment")]
    public Task<IActionResult> Delete(Guid stationId, [FromQuery] Guid expectedUserId, CancellationToken ct)
    {
        return Execute(async () =>
        {
            await client.DeleteAsync(new KitchenAssignmentRequest
            {
                StationId = stationId.ToString(),
                ExpectedUserId = expectedUserId.ToString()
            }, Options(ct));
            return Result(new { Message = "Đã gỡ phân công bếp." });
        }, ct);
    }

    private static KitchenAssignmentRequest Write(Guid stationId, KitchenAssignmentWriteDTO body)
    {
        return new KitchenAssignmentRequest
        {
            StationId = stationId.ToString(),
            UserId = body.UserId.ToString(),
            ExpectedUserId = body.ExpectedUserId.ToString()
        };
    }

    private static object Map(KitchenAssignmentInfo item)
    {
        return new
        {
            item.KitchenStationId,
            item.KitchenStationCode,
            item.KitchenStationName,
            item.StationActive,
            item.UserId,
            Employee = item.Employee is null ? null : Map(item.Employee)
        };
    }

    private static object Map(KitchenEmployeeInfo item)
    {
        return new
        {
            item.UserId,
            item.FullName,
            item.UserName,
            item.Email,
            item.AvatarUrl,
            item.RoleId,
            item.IsActive
        };
    }
}
