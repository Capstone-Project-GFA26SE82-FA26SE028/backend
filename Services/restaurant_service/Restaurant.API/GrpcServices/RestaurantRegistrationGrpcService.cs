using System.Globalization;
using Backend.Contracts.Restaurant;
using Google.Protobuf;
using Grpc.Core;
using Restaurant.API.Management;
using Restaurant.BLL.Implements;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.DAL.Models;

namespace Restaurant.API.GrpcServices;

public sealed class RestaurantRegistrationGrpcService(RestaurantRegistrationService service)
    : RestaurantRegistrationGrpc.RestaurantRegistrationGrpcBase
{
    public override async Task<RestaurantRegistrationReceipt> Create(RestaurantRegistrationCreateRequest request,
        ServerCallContext context)
    {
        if (request.Restaurant is null || request.Restaurant.HasLogoUrl)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Thông tin nhà hàng không hợp lệ."));
        }

        var data = await service.Create(ManagementMapping.Id(request.RegistrationId), ManagementMapping.ToDto(request.Restaurant),
            new RestaurantApplicantCredentials(request.Email, request.Password, request.FullName, request.PhoneNumber),
            request.BusinessCertificateImage.ToByteArray(), request.FoodSafetyCertificateImage.ToByteArray(), context.CancellationToken);
        return Receipt(data);
    }

    public override async Task<RestaurantRegistrationPage> GetAll(RestaurantRegistrationListRequest request, ServerCallContext context)
    {
        var (items, count) = await service.GetAll(request.HasStatus ? request.Status : null,
            request.Page, request.PageSize, context.CancellationToken);
        var result = new RestaurantRegistrationPage
        {
            TotalCount = count
        };
        result.Items.AddRange(items.Select(Map));
        return result;
    }

    public override async Task<RestaurantRegistrationInfo> GetById(IdRequest request, ServerCallContext context)
    {
        return Map(await service.GetById(ManagementMapping.Id(request.Id), context.CancellationToken));
    }

    public override async Task<RestaurantRegistrationDocument> GetDocument(RestaurantRegistrationDocumentRequest request, ServerCallContext context)
    {
        var (bytes, contentType) = await service.GetDocument(ManagementMapping.Id(request.RegistrationId), request.Kind, context.CancellationToken);
        return new RestaurantRegistrationDocument
        {
            Bytes = ByteString.CopyFrom(bytes),
            ContentType = contentType
        };
    }

    public override async Task<RestaurantRegistrationReceipt> Approve(IdRequest request, ServerCallContext context)
    {
        return Receipt(await service.Approve(ManagementMapping.Id(request.Id), context.CancellationToken));
    }

    private static RestaurantRegistrationReceipt Receipt(RestaurantRegistration value)
    {
        var result = new RestaurantRegistrationReceipt
        {
            RegistrationId = value.RegistrationId.ToString(),
            Status = value.Status
        };
        if (value.RestaurantId.HasValue)
        {
            result.RestaurantId = value.RestaurantId.Value.ToString();
        }

        return result;
    }

    private static RestaurantRegistrationInfo Map(RestaurantRegistration value)
    {
        var details = RestaurantRegistrationService.Details(value);
        var result = new RestaurantRegistrationInfo
        {
            Receipt = Receipt(value),
            Restaurant = ManagementMapping.ToWire(new RestaurantDto
            {
                RestaurantId = value.RestaurantId.GetValueOrDefault(),
                RestaurantName = details.RestaurantName,
                Address = details.Address,
                Hotline = details.Hotline,
                TaxCode = details.TaxCode,
                TimeZone = details.TimeZone,
                OpeningHours = details.OpeningHours
            }),
            Email = value.Email,
            ApplicantUserId = value.ApplicantUserRef.ToString(),
            SubmittedAt = DateTime.SpecifyKind(value.SubmittedAt, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture)
        };
        if (value.ReviewedAt.HasValue)
        {
            result.ReviewedAt = DateTime.SpecifyKind(value.ReviewedAt.Value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
        }

        if (value.ReviewedByRef.HasValue)
        {
            result.ReviewedBy = value.ReviewedByRef.Value.ToString();
        }

        return result;
    }
}
