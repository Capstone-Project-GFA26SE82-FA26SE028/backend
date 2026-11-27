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

    public override async Task<LoyaltyTierListReply> GetRestaurantTiers(RestaurantTierListRequest request, ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.RestaurantId, out var restaurantId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã nhà hàng không hợp lệ."));
            }

            var tiers = await service.GetTiersByRestaurant(restaurantId, context.CancellationToken);
            var reply = new LoyaltyTierListReply();
            reply.Items.AddRange(tiers.Select(CustomerMapping.Map));
            return reply;
        }
        catch (CustomerException ex)
        {
            throw new RpcException(new Status(MapStatusCode(ex.StatusCode), ex.Message));
        }
    }

    public override async Task<LoyaltyTierInfo> CreateTier(CreateLoyaltyTierRequest request, ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.RestaurantId, out var restaurantId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã nhà hàng không hợp lệ."));
            }

            decimal.TryParse(request.RequiredSpend, out var requiredSpend);
            decimal.TryParse(request.DiscountPercent, out var discountPercent);

            var tier = await service.CreateTier(restaurantId, new SaveLoyaltyTierDTO
            {
                LoyaltyTierCode = request.LoyaltyTierCode,
                LoyaltyTierName = request.LoyaltyTierName,
                RequiredSpend = requiredSpend,
                DiscountPercent = discountPercent
            }, context.CancellationToken);

            return CustomerMapping.Map(tier);
        }
        catch (CustomerException ex)
        {
            throw new RpcException(new Status(MapStatusCode(ex.StatusCode), ex.Message));
        }
    }

    public override async Task<LoyaltyTierInfo> UpdateTier(UpdateLoyaltyTierRequest request, ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.RestaurantId, out var restaurantId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã nhà hàng không hợp lệ."));
            }

            if (!Guid.TryParse(request.LoyaltyTierId, out var tierId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã hạng thành viên không hợp lệ."));
            }

            decimal.TryParse(request.RequiredSpend, out var requiredSpend);
            decimal.TryParse(request.DiscountPercent, out var discountPercent);

            var tier = await service.UpdateTier(restaurantId, tierId, new SaveLoyaltyTierDTO
            {
                LoyaltyTierId = tierId,
                LoyaltyTierCode = request.LoyaltyTierCode,
                LoyaltyTierName = request.LoyaltyTierName,
                RequiredSpend = requiredSpend,
                DiscountPercent = discountPercent
            }, context.CancellationToken);

            return CustomerMapping.Map(tier);
        }
        catch (CustomerException ex)
        {
            throw new RpcException(new Status(MapStatusCode(ex.StatusCode), ex.Message));
        }
    }

    public override async Task<Empty> DeleteTier(DeleteLoyaltyTierRequest request, ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.RestaurantId, out var restaurantId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã nhà hàng không hợp lệ."));
            }

            if (!Guid.TryParse(request.LoyaltyTierId, out var tierId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã hạng thành viên không hợp lệ."));
            }

            await service.DeleteTier(restaurantId, tierId, context.CancellationToken);
            return new Empty();
        }
        catch (CustomerException ex)
        {
            throw new RpcException(new Status(MapStatusCode(ex.StatusCode), ex.Message));
        }
    }

    public override async Task<LoyaltyPolicyInfo> GetRestaurantPolicy(RestaurantPolicyRequest request, ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.RestaurantId, out var restaurantId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã nhà hàng không hợp lệ."));
            }

            var policy = await service.GetPolicyByRestaurant(restaurantId, context.CancellationToken);
            return CustomerMapping.Map(policy);
        }
        catch (CustomerException ex)
        {
            throw new RpcException(new Status(MapStatusCode(ex.StatusCode), ex.Message));
        }
    }

    public override async Task<LoyaltyPolicyInfo> SavePolicy(SaveLoyaltyPolicyRequest request, ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.RestaurantId, out var restaurantId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã nhà hàng không hợp lệ."));
            }

            decimal.TryParse(request.SpendPerPoint, out var spendPerPoint);
            decimal.TryParse(request.MoneyPerRedeemedPoint, out var moneyPerPoint);

            var policy = await service.SavePolicy(restaurantId, new SaveLoyaltyPolicyDTO
            {
                SpendPerPoint = spendPerPoint,
                MoneyPerRedeemedPoint = moneyPerPoint
            }, context.CancellationToken);

            return CustomerMapping.Map(policy);
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

