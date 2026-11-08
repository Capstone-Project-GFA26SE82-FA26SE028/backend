using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace ApiGateway.Extensions;

public static class VietnameseValidationExtensions
{
    public static IMvcBuilder AddVietnameseValidation(this IMvcBuilder builder)
    {
        return builder.ConfigureApiBehaviorOptions(options =>
        {
            var defaultFactory = options.InvalidModelStateResponseFactory;
            options.InvalidModelStateResponseFactory = context =>
            {
                var controller = context.ActionDescriptor as ControllerActionDescriptor;
                if (controller?.ControllerTypeInfo.Namespace == "ApiGateway.Controllers.AI")
                {
                    return defaultFactory(context);
                }

                // Keep the framework response shape, status and trace ID.
                var result = defaultFactory(context);
                if (result is ObjectResult { Value: ValidationProblemDetails problem })
                {
                    problem.Title = "Dữ liệu yêu cầu không hợp lệ.";
                    foreach (var key in problem.Errors.Keys.ToArray())
                    {
                        problem.Errors[key] = problem.Errors[key].Select(message =>
                        {
                            if (!message.StartsWith("Trường ", StringComparison.Ordinal))
                            {
                                return "Giá trị không hợp lệ hoặc thiếu dữ liệu bắt buộc.";
                            }

                            return message;
                        }).ToArray();
                    }
                }

                return result;
            };
        });
    }
}
