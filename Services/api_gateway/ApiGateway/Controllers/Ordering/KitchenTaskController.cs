using ApiGateway.DTOs.Ordering;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Ordering;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ApiGateway.Controllers.Ordering;

[ApiController]
[Route("api/kitchen-tasks")]
[RequireBearerToken]
public sealed class KitchenTaskController(
    KitchenTaskGrpc.KitchenTaskGrpcClient client,
    IOptions<OrderingClientSettings> settings) : OrderingControllerBase(settings)
{
    [HttpGet]
    public Task<IActionResult> GetAll(
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] Guid? kitchenStationId = null,
        [FromQuery] string? status = null,
        [FromQuery] Guid? orderId = null,
        [FromQuery] Guid? tableSessionId = null)
    {
        return Execute(async () =>
        {
            var request = new KitchenTaskListRequest
            {
                Page = page,
                PageSize = pageSize
            };

            if (kitchenStationId.HasValue)
            {
                request.KitchenStationId = kitchenStationId.Value.ToString();
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                request.Status = status;
            }

            if (orderId.HasValue)
            {
                request.OrderId = orderId.Value.ToString();
            }

            if (tableSessionId.HasValue)
            {
                request.TableSessionId = tableSessionId.Value.ToString();
            }

            var reply = await client.GetAllAsync(request, Options(ct));
            return Result(new
            {
                Items = reply.Items.Select(OrderingMapping.Map).ToList(),
                reply.TotalCount,
                reply.Page,
                reply.PageSize
            });
        }, ct);
    }

    [HttpGet("summary")]
    public Task<IActionResult> GetSummary(CancellationToken ct, [FromQuery] Guid? kitchenStationId = null)
    {
        return Execute(async () =>
        {
            var request = new KitchenSummaryRequest();
            if (kitchenStationId.HasValue)
            {
                request.KitchenStationId = kitchenStationId.Value.ToString();
            }

            var reply = await client.GetSummaryAsync(request, Options(ct));
            return Result(OrderingMapping.Map(reply));
        }, ct);
    }

    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.GetByIdAsync(new KitchenTaskIdRequest
            {
                Id = id.ToString()
            }, Options(ct));

            return Result(OrderingMapping.Map(reply));
        }, ct);
    }

    [HttpPatch("{id:guid}/status")]
    public Task<IActionResult> SetStatus(Guid id, KitchenTaskStatusDTO body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var request = new KitchenTaskStatusRequest
            {
                Id = id.ToString(),
                Status = body.Status
            };

            if (!string.IsNullOrEmpty(body.Version))
            {
                request.Version = body.Version;
            }

            var reply = await client.SetStatusAsync(request, Options(ct));
            return Result(OrderingMapping.Map(reply));
        }, ct);
    }

    [HttpPatch("{id:guid}/priority")]
    public Task<IActionResult> SetPriority(Guid id, KitchenTaskPriorityDTO body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var request = new KitchenTaskPriorityRequest
            {
                Id = id.ToString(),
                Priority = body.Priority
            };

            if (!string.IsNullOrEmpty(body.Version))
            {
                request.Version = body.Version;
            }

            var reply = await client.SetPriorityAsync(request, Options(ct));
            return Result(OrderingMapping.Map(reply));
        }, ct);
    }

    [HttpGet("batches")]
    public Task<IActionResult> GetBatches(CancellationToken ct, [FromQuery] Guid? kitchenStationId = null)
    {
        return Execute(async () =>
        {
            var request = new KitchenBatchListRequest();
            if (kitchenStationId.HasValue)
            {
                request.KitchenStationId = kitchenStationId.Value.ToString();
            }

            var reply = await client.GetBatchesAsync(request, Options(ct));
            return Result(reply.Batches.Select(OrderingMapping.Map).ToList());
        }, ct);
    }

    [HttpPost("batches/start")]
    public Task<IActionResult> StartBatch([FromBody] KitchenBatchActionDTO body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var request = new KitchenBatchActionRequest();
            request.TaskIds.AddRange(body.TaskIds.Select(id => id.ToString()));

            var reply = await client.StartBatchAsync(request, Options(ct));
            return Result(OrderingMapping.Map(reply));
        }, ct);
    }

    [HttpPost("batches/complete")]
    public Task<IActionResult> CompleteBatch([FromBody] KitchenBatchActionDTO body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var request = new KitchenBatchActionRequest();
            request.TaskIds.AddRange(body.TaskIds.Select(id => id.ToString()));

            var reply = await client.CompleteBatchAsync(request, Options(ct));
            return Result(OrderingMapping.Map(reply));
        }, ct);
    }

    [HttpPost("{id:guid}/remake")]
    public Task<IActionResult> RemakeTask(Guid id, [FromBody] KitchenTaskRemakeDTO body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var request = new KitchenTaskRemakeRequest
            {
                Id = id.ToString(),
                Reason = body.Reason
            };

            var reply = await client.RemakeTaskAsync(request, Options(ct));
            return Result(OrderingMapping.Map(reply));
        }, ct);
    }

    [HttpPost("fire-course")]
    public Task<IActionResult> FireCourse([FromBody] FireCourseRequestDTO body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var request = new FireCourseRequest
            {
                TableSessionId = body.TableSessionId.ToString(),
                Course = body.Course
            };

            if (body.OrderId.HasValue)
            {
                request.OrderId = body.OrderId.Value.ToString();
            }

            var reply = await client.FireCourseAsync(request, Options(ct));
            return Result(OrderingMapping.Map(reply));
        }, ct);
    }
}

