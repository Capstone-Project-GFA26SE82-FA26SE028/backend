namespace Identity.BLL.Interfaces;

public sealed class RestaurantApplicantException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
