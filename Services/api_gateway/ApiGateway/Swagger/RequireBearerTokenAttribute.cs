namespace ApiGateway.Swagger;

// Swagger metadata only. Identity enforces authentication and authorization.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class RequireBearerTokenAttribute : Attribute
{
}
