using ApiGateway.DTOs.Restaurant;
using ApiGateway.Swagger;
using Backend.Contracts.Restaurant;
using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.DTOs;
using Shared.Common.Images;

namespace ApiGateway.Controllers.RestaurantManagement;

[ApiController]
[Route("api/restaurant-registrations")]
public sealed class RestaurantRegistrationController(RestaurantRegistrationGrpc.RestaurantRegistrationGrpcClient client)
    : ManagementControllerBase
{
    [HttpPost]
    [RequireBearerToken(Optional = true)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(2 * CloudinaryImageStorage.MaxImageBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = 2 * CloudinaryImageStorage.MaxImageBytes + 65536)]
    public Task<IActionResult> Create([FromForm] RestaurantRegistrationForm form, CancellationToken ct)
    {
        return Execute(async () =>
        {
            if (form.RegistrationId == Guid.Empty || !ValidFile(form.BusinessCertificateImage)
                || !ValidFile(form.FoodSafetyCertificateImage) || form.OpeningHours.Any(item => item is null)
                || !string.IsNullOrWhiteSpace(form.LogoUrl))
            {
                return BadRequest(new ResponseDTO("Cần mã hồ sơ hợp lệ và đủ hai ảnh giấy tờ, mỗi ảnh tối đa 2 MiB.", 400));
            }

            using var business = form.BusinessCertificateImage!.OpenReadStream();
            using var safety = form.FoodSafetyCertificateImage!.OpenReadStream();
            var request = new RestaurantRegistrationCreateRequest
            {
                RegistrationId = form.RegistrationId.ToString(),
                Email = form.Email ?? "",
                Password = form.Password ?? "",
                FullName = form.FullName ?? "",
                PhoneNumber = form.PhoneNumber ?? "",
                Restaurant = ManagementMapping.ToWire(new RestaurantDto
                {
                    RestaurantName = form.RestaurantName,
                    Address = form.Address,
                    Hotline = form.Hotline,
                    TaxCode = form.TaxCode,
                    TimeZone = form.TimeZone,
                    OpeningHours = form.OpeningHours
                }),
                BusinessCertificateImage = await ByteString.FromStreamAsync(business, ct),
                FoodSafetyCertificateImage = await ByteString.FromStreamAsync(safety, ct)
            };
            var result = await client.CreateAsync(request, Options(ct).WithDeadline(DateTime.UtcNow.AddSeconds(35)));
            return Receipt(result);
        }, ct, 202);
    }

    [HttpGet]
    [RequireBearerToken]
    public Task<IActionResult> GetAll(CancellationToken ct, int page = 1, int pageSize = 20, string? status = null)
    {
        return Execute(async () =>
        {
            var request = new RestaurantRegistrationListRequest
            {
                Page = page,
                PageSize = pageSize
            };
            if (status is not null)
            {
                request.Status = status;
            }

            var result = await client.GetAllAsync(request, Options(ct));
            return new
            {
                Items = result.Items.Select(Map).ToList(),
                result.TotalCount,
                Page = page,
                PageSize = pageSize
            };
        }, ct);
    }

    [HttpGet("{id:guid}")]
    [RequireBearerToken]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        return Execute(async () => Map(await client.GetByIdAsync(new IdRequest
        {
            Id = id.ToString()
        }, Options(ct))), ct);
    }

    [HttpGet("{id:guid}/documents/{kind}")]
    [RequireBearerToken]
    public Task<IActionResult> GetDocument(Guid id, string kind, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var result = await client.GetDocumentAsync(new RestaurantRegistrationDocumentRequest
            {
                RegistrationId = id.ToString(),
                Kind = kind
            }, Options(ct));
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(result.Bytes.ToByteArray(), result.ContentType);
        }, ct);
    }

    [HttpPost("{id:guid}/approve")]
    [RequireBearerToken]
    public Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        return Execute(async () => Receipt(await client.ApproveAsync(new IdRequest
        {
            Id = id.ToString()
        }, Options(ct))), ct);
    }

    private static bool ValidFile(IFormFile? file)
    {
        return file is { Length: > 0 and <= CloudinaryImageStorage.MaxImageBytes };
    }

    private static object Receipt(RestaurantRegistrationReceipt value)
    {
        return new
        {
            value.RegistrationId,
            value.Status,
            RestaurantId = value.HasRestaurantId ? value.RestaurantId : null
        };
    }

    private static object Map(RestaurantRegistrationInfo value)
    {
        return new
        {
            value.Receipt.RegistrationId,
            value.Receipt.Status,
            RestaurantId = value.Receipt.HasRestaurantId ? value.Receipt.RestaurantId : null,
            Restaurant = ManagementMapping.ToDto(value.Restaurant),
            value.Email,
            value.ApplicantUserId,
            value.SubmittedAt,
            ReviewedAt = value.HasReviewedAt ? value.ReviewedAt : null,
            ReviewedBy = value.HasReviewedBy ? value.ReviewedBy : null
        };
    }
}
