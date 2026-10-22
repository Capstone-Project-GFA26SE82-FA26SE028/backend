using ApiGateway.DTOs.Inventory;
using ApiGateway.Swagger;
using Backend.Contracts.Inventory;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers.Inventory;

[ApiController]
[Route("api/units")]
[RequireBearerToken]
public sealed class UnitController(UnitGrpc.UnitGrpcClient client) : InventoryControllerBase
{
    [HttpPost]
    public Task<IActionResult> Create(UnitWriteDTO body, CancellationToken ct)
    {
        return Execute(async () => Map(await client.CreateAsync(new UnitWriteRequest
        {
            Code = body.Code,
            Name = body.Name
        }, Options(ct))), ct, 201);
    }

    [HttpGet]
    public Task<IActionResult> GetAll(CancellationToken ct)
    {
        return Execute(async () => (await client.GetAllAsync(new UnitListRequest(), Options(ct))).Items.Select(Map).ToList(), ct);
    }

    private static object Map(UnitInfo unit)
    {
        return new
        {
            UnitId = unit.Id,
            unit.Code,
            unit.Name
        };
    }
}
