using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Grpc.Core;

namespace Billing.API.GrpcServices;

public sealed class PaymentProviderConfigGrpcService(IPaymentProviderConfigService service)
    : PaymentProviderConfigGrpc.PaymentProviderConfigGrpcBase
{
    public override async Task<PayOsConfigurationInfo> GetById(PayOsConfigurationRequest request, ServerCallContext context)
    {
        return Map(await service.GetById(Guid.Parse(request.RestaurantId), context.CancellationToken));
    }

    public override async Task<PayOsConfigurationInfo> Update(SavePayOsConfigurationRequest request, ServerCallContext context)
    {
        return Map(await service.Update(Guid.Parse(request.RestaurantId), new SavePayOsConfigurationDTO
        {
            ClientId = request.ClientId,
            ApiKey = request.ApiKey,
            ChecksumKey = request.ChecksumKey,
            ReturnUrl = request.ReturnUrl,
            CancelUrl = request.CancelUrl,
            Enabled = request.Enabled
        }, context.CancellationToken));
    }

    public override async Task<PayOsTestQrInfo> TestQr(TestPayOsConfigurationRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.RestaurantId, out var restaurantId) || restaurantId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Vui lòng chọn nhà hàng."));
        }

        try
        {
            var value = await service.TestQr(restaurantId, new TestPayOsConfigurationDTO
            {
                ClientId = request.ClientId,
                ApiKey = request.ApiKey,
                ChecksumKey = request.ChecksumKey,
                UseSavedConfiguration = request.UseSavedConfiguration
            }, context.CancellationToken);

            return new PayOsTestQrInfo
            {
                OrderCode = value.OrderCode,
                Amount = value.Amount,
                Description = "Kiem tra PayOS",
                QrCode = value.QrCode ?? string.Empty,
                CheckoutUrl = value.CheckoutUrl ?? string.Empty,
                PaymentLinkId = value.LinkId,
                Status = value.Status,
                AccountName = value.AccountName ?? string.Empty,
                AccountNumber = value.AccountNumber ?? string.Empty,
                Bin = value.Bin ?? string.Empty
            };
        }
        catch (PayOsClientException error) when (error.Failure == "ProviderRejected"
            || (error.Failure == "HttpError" && error.ProviderCode is "400" or "401" or "403"))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "PayOS từ chối tạo QR thử. Vui lòng kiểm tra bộ khóa và kênh thanh toán."));
        }
        catch (PayOsClientException)
        {
            throw new RpcException(new Status(StatusCode.Internal, "Không thể tạo QR thử qua PayOS."));
        }
    }

    private static PayOsConfigurationInfo Map(PayOsConfigurationDTO value)
    {
        return new PayOsConfigurationInfo
        {
            RestaurantId = value.RestaurantId.ToString(),
            Configured = value.Configured,
            ClientId = value.ClientId,
            ReturnUrl = value.ReturnUrl,
            CancelUrl = value.CancelUrl,
            WebhookUrl = value.WebhookUrl,
            Enabled = value.Enabled
        };
    }
}
