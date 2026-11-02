using System.Globalization;
using Backend.Contracts.Catalog;
using Catalog.BLL.Interfaces;
using Catalog.Common.DTOs;
using Grpc.Core;
using Shared.Common.DTOs;

namespace Catalog.API.GrpcServices;

public sealed class RecipeGrpcService(IRecipeService service) : RecipeGrpc.RecipeGrpcBase
{
    public override async Task<RecipeReply> CreateItemRecipe(
        CreateItemRecipeRequest request,
        ServerCallContext context)
    {
        var dto = new CreateRecipeVersionDTO
        {
            ItemId = ParseId(request.ItemId),
            EffectiveFrom = request.HasEffectiveFrom ? ParseDateTime(request.EffectiveFrom) : null,
            Instructions = request.HasInstructions ? request.Instructions : null,
            Ingredients = request.Ingredients.Select(i => new CreateRecipeIngredientDTO
            {
                IngredientRef = ParseId(i.IngredientRef),
                UnitRef = ParseId(i.UnitRef),
                Quantity = decimal.TryParse(i.Quantity, CultureInfo.InvariantCulture, out var qty) ? qty : 0m,
                ConversionFactor = decimal.TryParse(i.ConversionFactor, CultureInfo.InvariantCulture, out var factor) ? factor : null
            }).ToList(),
            Media = request.Media.Select(m => new CreateRecipeMediaDTO
            {
                MediaType = m.MediaType,
                Url = m.Url,
                DisplayOrder = m.DisplayOrder
            }).ToList()
        };

        var response = await service.CreateItemRecipe(
            ParseId(request.ItemId),
            dto,
            context.CancellationToken);

        return ToReply(response);
    }

    public override async Task<RecipeReply> GetActiveItemRecipe(
        ActiveItemRecipeRequest request,
        ServerCallContext context)
    {
        DateTime? at = request.HasAt ? ParseDateTime(request.At) : null;
        var response = await service.GetActiveItemRecipe(
            ParseId(request.ItemId),
            at,
            context.CancellationToken);

        return ToReply(response);
    }

    public override async Task<RecipeListReply> GetItemRecipeHistory(
        ItemRecipeHistoryRequest request,
        ServerCallContext context)
    {
        var response = await service.GetItemRecipeHistory(
            ParseId(request.ItemId),
            context.CancellationToken);

        var reply = new RecipeListReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? string.Empty,
            IsSuccess = response.IsSuccess
        };

        if (response.Result is List<RecipeVersionDTO> list)
        {
            reply.Items.AddRange(list.Select(Map));
        }

        return reply;
    }

    public override async Task<RecipeReply> CreateOptionRecipe(
        CreateOptionRecipeRequest request,
        ServerCallContext context)
    {
        var dto = new CreateRecipeVersionDTO
        {
            OptionId = ParseId(request.OptionId),
            EffectiveFrom = request.HasEffectiveFrom ? ParseDateTime(request.EffectiveFrom) : null,
            Instructions = request.HasInstructions ? request.Instructions : null,
            Ingredients = request.Ingredients.Select(i => new CreateRecipeIngredientDTO
            {
                IngredientRef = ParseId(i.IngredientRef),
                UnitRef = ParseId(i.UnitRef),
                Quantity = decimal.TryParse(i.Quantity, CultureInfo.InvariantCulture, out var qty) ? qty : 0m,
                ConversionFactor = decimal.TryParse(i.ConversionFactor, CultureInfo.InvariantCulture, out var factor) ? factor : null
            }).ToList(),
            Media = request.Media.Select(m => new CreateRecipeMediaDTO
            {
                MediaType = m.MediaType,
                Url = m.Url,
                DisplayOrder = m.DisplayOrder
            }).ToList()
        };

        var response = await service.CreateOptionRecipe(
            ParseId(request.OptionId),
            dto,
            context.CancellationToken);

        return ToReply(response);
    }

    public override async Task<RecipeReply> GetActiveOptionRecipe(
        ActiveOptionRecipeRequest request,
        ServerCallContext context)
    {
        DateTime? at = request.HasAt ? ParseDateTime(request.At) : null;
        var response = await service.GetActiveOptionRecipe(
            ParseId(request.OptionId),
            at,
            context.CancellationToken);

        return ToReply(response);
    }

    public override async Task<RecipeListReply> GetOptionRecipeHistory(
        OptionRecipeHistoryRequest request,
        ServerCallContext context)
    {
        var response = await service.GetOptionRecipeHistory(
            ParseId(request.OptionId),
            context.CancellationToken);

        var reply = new RecipeListReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? string.Empty,
            IsSuccess = response.IsSuccess
        };

        if (response.Result is List<RecipeVersionDTO> list)
        {
            reply.Items.AddRange(list.Select(Map));
        }

        return reply;
    }

    public override async Task<RecipeReply> GetRecipeById(
        RecipeIdRequest request,
        ServerCallContext context)
    {
        var response = await service.GetById(
            ParseId(request.RecipeVersionId),
            context.CancellationToken);

        return ToReply(response);
    }

    private static RecipeReply ToReply(ResponseDTO response)
    {
        var reply = new RecipeReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? string.Empty,
            IsSuccess = response.IsSuccess
        };

        if (response.Result is RecipeVersionDTO result)
        {
            reply.Result = Map(result);
        }

        return reply;
    }

    private static RecipeInfo Map(RecipeVersionDTO dto)
    {
        var info = new RecipeInfo
        {
            RecipeVersionId = dto.RecipeVersionId.ToString(),
            Version = dto.Version,
            EffectiveFrom = DateTime.SpecifyKind(dto.EffectiveFrom, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
            Instructions = dto.Instructions ?? string.Empty
        };

        if (dto.ItemId.HasValue)
        {
            info.ItemId = dto.ItemId.Value.ToString();
        }

        if (dto.OptionId.HasValue)
        {
            info.OptionId = dto.OptionId.Value.ToString();
        }

        if (dto.EffectiveTo.HasValue)
        {
            info.EffectiveTo = DateTime.SpecifyKind(dto.EffectiveTo.Value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
        }

        foreach (var ingredient in dto.Ingredients)
        {
            info.Ingredients.Add(new RecipeIngredientInfo
            {
                RecipeIngredientId = ingredient.RecipeIngredientId.ToString(),
                RecipeVersionId = ingredient.RecipeVersionId.ToString(),
                IngredientRef = ingredient.IngredientRef.ToString(),
                UnitRef = ingredient.UnitRef.ToString(),
                Quantity = ingredient.Quantity.ToString("0.######", CultureInfo.InvariantCulture),
                BaseQuantitySnapshot = ingredient.BaseQuantitySnapshot.ToString("0.######", CultureInfo.InvariantCulture),
                ConversionFactorSnapshot = ingredient.ConversionFactorSnapshot.ToString("0.######", CultureInfo.InvariantCulture)
            });
        }

        foreach (var media in dto.Media)
        {
            info.Media.Add(new RecipeMediaInfo
            {
                RecipeMediaId = media.RecipeMediaId.ToString(),
                RecipeVersionId = media.RecipeVersionId.ToString(),
                MediaType = media.MediaType,
                Url = media.Url,
                DisplayOrder = media.DisplayOrder
            });
        }

        return info;
    }

    private static Guid ParseId(string? value)
    {
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }

    private static DateTime? ParseDateTime(string? value)
    {
        string[] formats =
        [
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
            "yyyy-MM-dd'T'HH:mm:sszzz",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"
        ];
        if (DateTimeOffset.TryParseExact(value, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var offset))
        {
            return offset.UtcDateTime;
        }

        throw new RpcException(new Status(StatusCode.InvalidArgument, "Mốc thời gian công thức không hợp lệ."));
    }
}
