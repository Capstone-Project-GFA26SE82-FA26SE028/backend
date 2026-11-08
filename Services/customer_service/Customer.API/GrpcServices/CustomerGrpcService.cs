using Backend.Contracts.Customer;
using Customer.BLL.Interfaces;
using Customer.Common.DTOs;
using Customer.Common.Exceptions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Customer.API.GrpcServices;

public sealed class CustomerGrpcService(ICustomerLoyaltyService service) : Backend.Contracts.Customer.CustomerGrpc.CustomerGrpcBase
{
    public override async Task<CustomerInfo> GetOrCreate(CreateCustomerRequest request, ServerCallContext context)
    {
        try
        {
            var customer = await service.GetOrCreate(new CreateCustomerDTO
            {
                PhoneNumber = request.Phone,
                CustomerName = request.Name
            }, context.CancellationToken);

            return CustomerMapping.Map(customer);
        }
        catch (CustomerException ex)
        {
            throw new RpcException(new Status(MapStatusCode(ex.StatusCode), ex.Message));
        }
    }

    public override async Task<CustomerInfo> GetByPhone(PhoneRequest request, ServerCallContext context)
    {
        try
        {
            var customer = await service.GetByPhone(request.Phone, context.CancellationToken);
            if (customer is null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, "Không tìm thấy khách hàng."));
            }

            return CustomerMapping.Map(customer);
        }
        catch (CustomerException ex)
        {
            throw new RpcException(new Status(MapStatusCode(ex.StatusCode), ex.Message));
        }
    }

    public override async Task<CustomerInfo> GetById(CustomerIdRequest request, ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.CustomerId, out var id))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã khách hàng không hợp lệ."));
            }

            var customer = await service.GetById(id, context.CancellationToken);
            if (customer is null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, "Không tìm thấy khách hàng."));
            }

            return CustomerMapping.Map(customer);
        }
        catch (CustomerException ex)
        {
            throw new RpcException(new Status(MapStatusCode(ex.StatusCode), ex.Message));
        }
    }

    public override async Task<LoyaltyTierListReply> GetTiers(Empty request, ServerCallContext context)
    {
        var tiers = await service.GetTiers(context.CancellationToken);
        var reply = new LoyaltyTierListReply();
        reply.Items.AddRange(tiers.Select(CustomerMapping.Map));
        return reply;
    }

    public override async Task<EarnPointsReply> EarnPoints(EarnPointsRequest request, ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.CustomerId, out var customerId) || !Guid.TryParse(request.BillId, out var billId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã khách hàng hoặc mã hóa đơn không hợp lệ."));
            }

            decimal.TryParse(request.FinalAmount, out var finalAmount);

            var result = await service.EarnPoints(new LoyaltyEarnRequestDTO
            {
                CustomerId = customerId,
                BillId = billId,
                FinalAmount = finalAmount
            }, context.CancellationToken);

            return new EarnPointsReply
            {
                PointsEarned = result.PointsEarned,
                NewTotalPoints = result.NewTotalPoints,
                LoyaltyTierName = result.LoyaltyTierName,
                TierUpgraded = result.TierUpgraded
            };
        }
        catch (CustomerException ex)
        {
            throw new RpcException(new Status(MapStatusCode(ex.StatusCode), ex.Message));
        }
    }

    public override async Task<ReservePointsReply> ReservePoints(ReservePointsRequest request, ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.CustomerId, out var customerId) || !Guid.TryParse(request.BillId, out var billId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã khách hàng hoặc mã hóa đơn không hợp lệ."));
            }

            var result = await service.ReservePoints(new PointReservationRequestDTO
            {
                CustomerId = customerId,
                BillId = billId,
                Points = request.Points
            }, context.CancellationToken);

            return new ReservePointsReply
            {
                PointReservationId = result.PointReservationId.ToString(),
                ReservedPoints = result.ReservedPoints,
                DiscountAmount = result.DiscountAmount.ToString("0.##"),
                RemainingPoints = result.RemainingPoints
            };
        }
        catch (CustomerException ex)
        {
            throw new RpcException(new Status(MapStatusCode(ex.StatusCode), ex.Message));
        }
    }

    public override async Task<Empty> CommitPoints(BillIdRequest request, ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.BillId, out var billId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã hóa đơn không hợp lệ."));
            }

            await service.CommitPoints(billId, context.CancellationToken);
            return new Empty();
        }
        catch (CustomerException ex)
        {
            throw new RpcException(new Status(MapStatusCode(ex.StatusCode), ex.Message));
        }
    }

    public override async Task<Empty> CancelPoints(BillIdRequest request, ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.BillId, out var billId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã hóa đơn không hợp lệ."));
            }

            await service.CancelPoints(billId, context.CancellationToken);
            return new Empty();
        }
        catch (CustomerException ex)
        {
            throw new RpcException(new Status(MapStatusCode(ex.StatusCode), ex.Message));
        }
    }

    private static StatusCode MapStatusCode(int code)
    {
        return code switch
        {
            400 => StatusCode.InvalidArgument,
            401 => StatusCode.Unauthenticated,
            403 => StatusCode.PermissionDenied,
            404 => StatusCode.NotFound,
            409 => StatusCode.AlreadyExists,
            _ => StatusCode.Internal
        };
    }
}

