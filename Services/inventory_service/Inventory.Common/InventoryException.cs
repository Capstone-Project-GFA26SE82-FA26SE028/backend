namespace Inventory.Common;

public sealed class InventoryException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
