using ApiGateway.DTOs.Restaurant;
using ApiGateway.Swagger;
using Backend.Contracts.Restaurant;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers.RestaurantManagement;

[ApiController]
[Route("api/restaurants/{restaurantId}/staff")]
[RequireBearerToken]
public sealed class RestaurantStaffController(RestaurantStaffGrpc.RestaurantStaffGrpcClient client) : ReservationControllerBase
{
    [HttpPost]
    public Task<IActionResult> Create(Guid restaurantId, AssignRestaurantStaffDto body, CancellationToken ct)
    {
        return Execute(async () => Map(await client.CreateAsync(new RestaurantStaffRequest
        {
            RestaurantId = restaurantId.ToString(),
            UserIdRef = body.UserIdRef.ToString()
        }, Options(ct))), ct, "Restaurant assignment created successfully.", 201);
    }

    [HttpGet]
    public Task<IActionResult> GetAll(Guid restaurantId, CancellationToken ct)
    {
        return Execute(async () => (await client.GetAllAsync(new RestaurantStaffListRequest
        {
            RestaurantId = restaurantId.ToString()
        }, Options(ct))).Items.Select(Map).ToArray(), ct, "Restaurant assignments retrieved successfully.");
    }

    [HttpDelete("{userIdRef}")]
    public Task<IActionResult> Delete(Guid restaurantId, Guid userIdRef, CancellationToken ct)
    {
        return Execute(async () => Map(await client.DeleteAsync(new RestaurantStaffRequest
        {
            RestaurantId = restaurantId.ToString(),
            UserIdRef = userIdRef.ToString()
        }, Options(ct))), ct, "Restaurant assignment revoked successfully.");
    }

    private static RestaurantStaffDto Map(RestaurantStaffInfo value)
    {
        return new RestaurantStaffDto
        {
            RestaurantId = Guid.Parse(value.RestaurantId),
            UserIdRef = Guid.Parse(value.UserIdRef),
            IsActive = value.IsActive
        };
    }
}
