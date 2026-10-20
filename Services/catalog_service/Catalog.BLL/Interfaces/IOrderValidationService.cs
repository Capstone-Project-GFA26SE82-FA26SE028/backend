using Catalog.Common.DTOs;
using Shared.Common.DTOs;

namespace Catalog.BLL.Interfaces;

public interface IOrderValidationService
{
    Task<ResponseDTO> ValidateOrderItems(ValidateOrderItemsDTO request, Guid? tableSessionId,
        string? guestToken, CancellationToken cancellationToken);
}
