using System.Text.Json;

namespace Shared.Common.DTOs;

public class ResponseDTO<T>
{
    public int StatusCode { get; set; }
    public string? Message { get; set; }
    public bool IsSuccess { get; set; }
    public T? Result { get; set; }

    public ResponseDTO()
    {
    }

    public ResponseDTO(string message, int statusCode, bool success = false, T? result = default)
    {
        Message = message;
        StatusCode = statusCode;
        IsSuccess = success;
        Result = result;
    }

    public override string ToString()
    {
        return JsonSerializer.Serialize(this);
    }
}

public class ResponseDTO : ResponseDTO<object>
{
    public ResponseDTO()
    {
    }

    public ResponseDTO(string message, int statusCode, bool success = false, object? result = null)
        : base(message, statusCode, success, result)
    {
    }
}