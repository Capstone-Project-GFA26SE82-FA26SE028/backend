using System.Globalization;
using ApiGateway.DTOs.Inventory;
using ApiGateway.Swagger;
using Backend.Contracts.Inventory;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers.Inventory;

[ApiController]
[Route("api/ingredients/{ingredientId:guid}/units")]
[RequireBearerToken]
public sealed class IngredientUnitController(IngredientUnitGrpc.IngredientUnitGrpcClient client) : InventoryControllerBase
{
    [HttpPut("{unitId:guid}")]
    public Task<IActionResult> Update(Guid ingredientId, Guid unitId, ConversionWriteDTO body, CancellationToken ct)
    {
        return Execute(async () => Map(await client.UpdateAsync(new ConversionWriteRequest
        {
            IngredientId = ingredientId.ToString(),
            UnitId = unitId.ToString(),
            Factor = body.ConversionFactor.ToString(CultureInfo.InvariantCulture)
        }, Options(ct))), ct);
    }

    [HttpGet]
    public Task<IActionResult> GetAll(Guid ingredientId, CancellationToken ct)
    {
        return Execute(async () => (await client.GetAllAsync(new InventoryIdRequest
        {
            Id = ingredientId.ToString()
        }, Options(ct))).Items.Select(Map).ToList(), ct);
    }

    private static object Map(ConversionInfo conversion)
    {
        return new
        {
            conversion.IngredientId,
            conversion.UnitId,
            ConversionFactor = decimal.Parse(conversion.Factor, CultureInfo.InvariantCulture)
        };
    }
}
