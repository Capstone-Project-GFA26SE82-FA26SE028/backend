namespace Customer.Common.Exceptions;

public sealed class CustomerException : Exception
{
    public int StatusCode { get; }

    public CustomerException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }
}

