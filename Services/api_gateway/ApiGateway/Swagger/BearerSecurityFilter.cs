using System.Reflection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ApiGateway.Swagger;

public sealed class BearerSecurityFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var attribute = context.MethodInfo.GetCustomAttribute<RequireBearerTokenAttribute>(true)
            ?? context.MethodInfo.DeclaringType?.GetCustomAttribute<RequireBearerTokenAttribute>(true);
        if (attribute is null)
        {
            return;
        }

        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            }] = Array.Empty<string>()
        });
        if (attribute.Optional)
        {
            operation.Security.Add(new OpenApiSecurityRequirement());
        }
    }
}
