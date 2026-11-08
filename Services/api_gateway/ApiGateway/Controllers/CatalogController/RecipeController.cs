using System.Globalization;
using ApiGateway.DTOs.Catalog;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Catalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.CatalogController;

[ApiController]
[RequireBearerToken]
public sealed class RecipeController(
    RecipeGrpc.RecipeGrpcClient client,
    IOptions<CatalogClientSettings> settings) : CatalogControllerBase(settings)
{
    [HttpPost("api/items/{itemId:guid}/recipes")]
    public Task<IActionResult> Create(
        Guid itemId,
        CreateRecipeVersionDTO body,
        CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var request = new CreateItemRecipeRequest
            {
                ItemId = itemId.ToString()
            };

            if (body.EffectiveFrom.HasValue)
            {
                request.EffectiveFrom = body.EffectiveFrom.Value.ToString("o", CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrWhiteSpace(body.Instructions))
            {
                request.Instructions = body.Instructions;
            }

            if (body.Ingredients is not null)
            {
                foreach (var ingredient in body.Ingredients)
                {
                    var ingReq = new CreateRecipeIngredientRequest
                    {
                        IngredientRef = ingredient.IngredientRef.ToString(),
                        UnitRef = ingredient.UnitRef.ToString(),
                        Quantity = ingredient.Quantity.ToString(CultureInfo.InvariantCulture)
                    };

                    if (ingredient.ConversionFactor.HasValue)
                    {
                        ingReq.ConversionFactor = ingredient.ConversionFactor.Value.ToString(CultureInfo.InvariantCulture);
                    }

                    request.Ingredients.Add(ingReq);
                }
            }

            if (body.Media is not null)
            {
                foreach (var media in body.Media)
                {
                    request.Media.Add(new CreateRecipeMediaRequest
                    {
                        MediaType = media.MediaType ?? string.Empty,
                        Url = media.Url ?? string.Empty,
                        DisplayOrder = media.DisplayOrder
                    });
                }
            }

            var reply = await client.CreateItemRecipeAsync(request, Options(cancellationToken));

            var result = reply.Result is null ? null : Map(reply.Result);
            var response = new ResponseDTO
            {
                StatusCode = reply.StatusCode,
                Message = reply.Message,
                IsSuccess = reply.IsSuccess,
                Result = result
            };

            if (reply.StatusCode == 201 && result is not null)
            {
                Response.Headers.Location = $"/api/recipes/{Uri.EscapeDataString(result.RecipeVersionId.ToString())}";
            }

            return StatusCode(reply.StatusCode, response);
        }, cancellationToken);
    }

    [HttpGet("api/items/{itemId:guid}/recipes/active")]
    public Task<IActionResult> GetActive(
        Guid itemId,
        [FromQuery] DateTime? at,
        CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var request = new ActiveItemRecipeRequest
            {
                ItemId = itemId.ToString()
            };

            if (at.HasValue)
            {
                request.At = at.Value.ToString("o", CultureInfo.InvariantCulture);
            }

            var reply = await client.GetActiveItemRecipeAsync(request, Options(cancellationToken));

            return StatusCode(reply.StatusCode, new ResponseDTO
            {
                StatusCode = reply.StatusCode,
                Message = reply.Message,
                IsSuccess = reply.IsSuccess,
                Result = reply.Result is null ? null : Map(reply.Result)
            });
        }, cancellationToken);
    }

    [HttpGet("api/items/{itemId:guid}/recipes")]
    public Task<IActionResult> GetHistory(
        Guid itemId,
        CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var request = new ItemRecipeHistoryRequest
            {
                ItemId = itemId.ToString()
            };

            var reply = await client.GetItemRecipeHistoryAsync(request, Options(cancellationToken));

            return StatusCode(reply.StatusCode, new ResponseDTO
            {
                StatusCode = reply.StatusCode,
                Message = reply.Message,
                IsSuccess = reply.IsSuccess,
                Result = reply.Items.Select(Map).ToList()
            });
        }, cancellationToken);
    }

    [HttpPost("api/options/{optionId:guid}/recipes")]
    public Task<IActionResult> CreateForOption(
        Guid optionId,
        CreateRecipeVersionDTO body,
        CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var request = new CreateOptionRecipeRequest
            {
                OptionId = optionId.ToString()
            };

            if (body.EffectiveFrom.HasValue)
            {
                request.EffectiveFrom = body.EffectiveFrom.Value.ToString("o", CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrWhiteSpace(body.Instructions))
            {
                request.Instructions = body.Instructions;
            }

            if (body.Ingredients is not null)
            {
                foreach (var ingredient in body.Ingredients)
                {
                    var ingReq = new CreateRecipeIngredientRequest
                    {
                        IngredientRef = ingredient.IngredientRef.ToString(),
                        UnitRef = ingredient.UnitRef.ToString(),
                        Quantity = ingredient.Quantity.ToString(CultureInfo.InvariantCulture)
                    };

                    if (ingredient.ConversionFactor.HasValue)
                    {
                        ingReq.ConversionFactor = ingredient.ConversionFactor.Value.ToString(CultureInfo.InvariantCulture);
                    }

                    request.Ingredients.Add(ingReq);
                }
            }

            if (body.Media is not null)
            {
                foreach (var media in body.Media)
                {
                    request.Media.Add(new CreateRecipeMediaRequest
                    {
                        MediaType = media.MediaType ?? string.Empty,
                        Url = media.Url ?? string.Empty,
                        DisplayOrder = media.DisplayOrder
                    });
                }
            }

            var reply = await client.CreateOptionRecipeAsync(request, Options(cancellationToken));

            var result = reply.Result is null ? null : Map(reply.Result);
            var response = new ResponseDTO
            {
                StatusCode = reply.StatusCode,
                Message = reply.Message,
                IsSuccess = reply.IsSuccess,
                Result = result
            };

            if (reply.StatusCode == 201 && result is not null)
            {
                Response.Headers.Location = $"/api/recipes/{Uri.EscapeDataString(result.RecipeVersionId.ToString())}";
            }

            return StatusCode(reply.StatusCode, response);
        }, cancellationToken);
    }

    [HttpGet("api/options/{optionId:guid}/recipes/active")]
    public Task<IActionResult> GetActiveForOption(
        Guid optionId,
        [FromQuery] DateTime? at,
        CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var request = new ActiveOptionRecipeRequest
            {
                OptionId = optionId.ToString()
            };

            if (at.HasValue)
            {
                request.At = at.Value.ToString("o", CultureInfo.InvariantCulture);
            }

            var reply = await client.GetActiveOptionRecipeAsync(request, Options(cancellationToken));

            return StatusCode(reply.StatusCode, new ResponseDTO
            {
                StatusCode = reply.StatusCode,
                Message = reply.Message,
                IsSuccess = reply.IsSuccess,
                Result = reply.Result is null ? null : Map(reply.Result)
            });
        }, cancellationToken);
    }

    [HttpGet("api/options/{optionId:guid}/recipes")]
    public Task<IActionResult> GetHistoryForOption(
        Guid optionId,
        CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var request = new OptionRecipeHistoryRequest
            {
                OptionId = optionId.ToString()
            };

            var reply = await client.GetOptionRecipeHistoryAsync(request, Options(cancellationToken));

            return StatusCode(reply.StatusCode, new ResponseDTO
            {
                StatusCode = reply.StatusCode,
                Message = reply.Message,
                IsSuccess = reply.IsSuccess,
                Result = reply.Items.Select(Map).ToList()
            });
        }, cancellationToken);
    }

    [HttpGet("api/recipes/{recipeVersionId:guid}")]
    public Task<IActionResult> GetById(
        Guid recipeVersionId,
        CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var request = new RecipeIdRequest
            {
                RecipeVersionId = recipeVersionId.ToString()
            };

            var reply = await client.GetRecipeByIdAsync(request, Options(cancellationToken));

            return StatusCode(reply.StatusCode, new ResponseDTO
            {
                StatusCode = reply.StatusCode,
                Message = reply.Message,
                IsSuccess = reply.IsSuccess,
                Result = reply.Result is null ? null : Map(reply.Result)
            });
        }, cancellationToken);
    }

    private static RecipeVersionDTO Map(RecipeInfo info)
    {
        var dto = new RecipeVersionDTO
        {
            RecipeVersionId = Guid.TryParse(info.RecipeVersionId, out var rid) ? rid : Guid.Empty,
            ItemId = Guid.TryParse(info.ItemId, out var iid) ? iid : null,
            OptionId = Guid.TryParse(info.OptionId, out var oid) ? oid : null,
            Version = info.Version,
            EffectiveFrom = DateTime.TryParse(info.EffectiveFrom, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ef) ? ef : DateTime.MinValue,
            EffectiveTo = DateTime.TryParse(info.EffectiveTo, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var et) ? et : null,
            Instructions = info.Instructions
        };

        foreach (var ingredient in info.Ingredients)
        {
            dto.Ingredients.Add(new RecipeIngredientDTO
            {
                RecipeIngredientId = Guid.TryParse(ingredient.RecipeIngredientId, out var riid) ? riid : Guid.Empty,
                RecipeVersionId = Guid.TryParse(ingredient.RecipeVersionId, out var rvid) ? rvid : Guid.Empty,
                IngredientRef = Guid.TryParse(ingredient.IngredientRef, out var ir) ? ir : Guid.Empty,
                UnitRef = Guid.TryParse(ingredient.UnitRef, out var ur) ? ur : Guid.Empty,
                Quantity = decimal.TryParse(ingredient.Quantity, CultureInfo.InvariantCulture, out var q) ? q : 0m,
                BaseQuantitySnapshot = decimal.TryParse(ingredient.BaseQuantitySnapshot, CultureInfo.InvariantCulture, out var bq) ? bq : 0m,
                ConversionFactorSnapshot = decimal.TryParse(ingredient.ConversionFactorSnapshot, CultureInfo.InvariantCulture, out var cf) ? cf : 0m
            });
        }

        foreach (var media in info.Media)
        {
            dto.Media.Add(new RecipeMediaDTO
            {
                RecipeMediaId = Guid.TryParse(media.RecipeMediaId, out var rmid) ? rmid : Guid.Empty,
                RecipeVersionId = Guid.TryParse(media.RecipeVersionId, out var rvm) ? rvm : Guid.Empty,
                MediaType = media.MediaType,
                Url = media.Url,
                DisplayOrder = media.DisplayOrder
            });
        }

        return dto;
    }
}
