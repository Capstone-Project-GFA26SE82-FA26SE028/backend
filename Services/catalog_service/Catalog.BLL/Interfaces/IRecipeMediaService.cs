using Catalog.Common.DTOs;

namespace Catalog.BLL.Interfaces;

public interface IRecipeMediaService
{
    Task<RecipeMediaUploadDTO> Upload(byte[] file, CancellationToken ct);
}
