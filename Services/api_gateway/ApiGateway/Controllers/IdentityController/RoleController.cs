using ApiGateway.DTOs.Identity;
using ApiGateway.Swagger;
using Backend.Contracts.Identity;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.IdentityController;

[ApiController]
[Route("api/roles")]
[RequireBearerToken]
public sealed class RoleController(RoleGrpc.RoleGrpcClient client) : IdentityControllerBase
{
    [HttpGet]
    public Task<IActionResult> GetAll(CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.GetAllAsync(new ListRolesRequest(), Options(ct));
            return StatusCode(reply.StatusCode, new ResponseDTO
            {
                StatusCode = reply.StatusCode,
                Message = reply.Message,
                IsSuccess = reply.IsSuccess,
                Result = reply.IsSuccess ? reply.Items.Select(Map).ToArray() : null
            });
        }, ct);
    }

    [HttpGet("{id}")]
    public Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.GetByIdAsync(new RoleIdRequest
            {
                RoleId = id
            }, Options(ct));
            return ToResponse(reply);
        }, ct);
    }

    [HttpPost]
    public Task<IActionResult> Create(CreateRoleDTO body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.CreateAsync(ToRequest(body.RoleId, body), Options(ct));
            if (reply.StatusCode == 201 && reply.Result is not null)
            {
                Response.Headers.Location = $"/api/roles/{Uri.EscapeDataString(reply.Result.RoleId)}";
            }

            return ToResponse(reply);
        }, ct);
    }

    [HttpPut("{id}")]
    public Task<IActionResult> Update(string id, UpdateRoleDTO body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.UpdateAsync(ToRequest(id, body), Options(ct));
            return ToResponse(reply);
        }, ct);
    }

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.DeleteAsync(new RoleIdRequest
            {
                RoleId = id
            }, Options(ct));
            return ToResponse(reply);
        }, ct);
    }

    private static RoleInfo ToRequest(string id, UpdateRoleDTO body)
    {
        var request = new RoleInfo
        {
            RoleId = id,
            RoleName = body.RoleName
        };
        if (body.Description is not null)
        {
            request.Description = body.Description;
        }

        return request;
    }

    private IActionResult ToResponse(RoleReply reply)
    {
        return StatusCode(reply.StatusCode, new ResponseDTO
        {
            StatusCode = reply.StatusCode,
            Message = reply.Message,
            IsSuccess = reply.IsSuccess,
            Result = reply.Result is null ? null : Map(reply.Result)
        });
    }

    private static RoleDTO Map(RoleInfo role)
    {
        return new RoleDTO
        {
            RoleId = role.RoleId,
            RoleName = role.RoleName,
            Description = role.HasDescription ? role.Description : null
        };
    }
}

