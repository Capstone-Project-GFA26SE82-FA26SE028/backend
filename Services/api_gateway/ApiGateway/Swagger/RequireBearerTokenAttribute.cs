namespace ApiGateway.Swagger;

// Swagger metadata only. Identity enforces authentication and authorization.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class RequireBearerTokenAttribute : Attribute
{
    // Endpoints with another credential mechanism, such as a guest-session cookie.
    public bool Optional { get; set; }
}
