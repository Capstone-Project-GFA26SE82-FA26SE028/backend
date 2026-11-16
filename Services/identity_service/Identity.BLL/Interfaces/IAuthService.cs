using Shared.Common.DTOs;
using Identity.Common.DTOs;
using static Identity.Common.DTOs.AuthDTO;
namespace Identity.BLL.Interfaces;

public interface IAuthService
{
    Task<ResponseDTO> Register(RegisterCustomerDTO request, CancellationToken ct);

    Task<ResponseDTO> ChangePassword(Guid userId, ChangePasswordDTO request, CancellationToken ct);
    Task<ResponseDTO> Login(LoginDTO loginDTO);
    Task<ResponseDTO> Refresh(RefreshDTO request);
    Task<ResponseDTO> Logout(Guid sessionId, Guid userId);
}
