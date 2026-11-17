using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ApiGateway.Swagger;

public sealed class RestaurantScopeFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var type = context.MethodInfo.DeclaringType;
        var scoped = false;
        while (type is not null)
        {
            if (type.Name is "CatalogControllerBase" or "InventoryControllerBase" or "OrderingControllerBase"
                or "BillingControllerBase" or "CustomerControllerBase")
            {
                scoped = true;
                break;
            }

            type = type.BaseType;
        }

        if (!scoped)
        {
            return;
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Restaurant-Id",
            In = ParameterLocation.Header,
            Required = false,
            Description = "Nhà hàng đang thao tác. Bắt buộc khi không xác định được từ phiên bàn hoặc phân công duy nhất. Máy chủ kiểm tra quyền truy cập.",
            Schema = new OpenApiSchema
            {
                Type = "string",
                Format = "uuid"
            }
        });
    }
}
