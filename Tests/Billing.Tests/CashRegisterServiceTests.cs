using System.Data;
using Billing.BLL.Implements;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.Extensions.Options;
using Shared.Common.Pagination;
using Shared.Persistence.Interfaces;

namespace Billing.Tests;

public sealed class CashRegisterServiceTests
{
    private readonly Guid firstRestaurant = Guid.NewGuid();
    private readonly Guid secondRestaurant = Guid.NewGuid();

    [Theory]
    [InlineData("admin", true)]
    [InlineData("manager", false)]
    public async Task AuthorizedCreatorPersistsBranchAndTrimmedCode(string role, bool administrator)
    {
        var fixture = CreateFixture(role, administrator, [firstRestaurant]);
        var result = await fixture.Service.Create(new CreateCashRegisterDTO
        {
            RestaurantId = firstRestaurant,
            Code = "  POS-01  "
        }, default);

        Assert.Equal(firstRestaurant, result.RestaurantId);
        Assert.Equal("POS-01", result.Code);
        Assert.NotEqual(Guid.Empty, result.CashRegisterId);
        Assert.Equal(firstRestaurant, fixture.Repository.LockedRestaurant);
        Assert.Equal(1, fixture.UnitOfWork.TransactionCount);
        Assert.Equal(new Guid?[] { null, firstRestaurant }, fixture.Access.RequestedRestaurants);
    }

    [Theory]
    [InlineData("cashier")]
    [InlineData("waiter")]
    [InlineData("kitchen")]
    [InlineData("inventory")]
    public async Task UnauthorizedRoleCannotCreateEvenWhenAssigned(string role)
    {
        var fixture = CreateFixture(role, false, [firstRestaurant]);
        await AssertStatus(403, () => fixture.Service.Create(Request(firstRestaurant, "POS-01"), default));
        Assert.Empty(fixture.Repository.Values);
        Assert.Equal(0, fixture.UnitOfWork.TransactionCount);
    }

    [Fact]
    public async Task ManagerCannotCreateAtAnotherRestaurant()
    {
        var fixture = CreateFixture("manager", false, [firstRestaurant]);
        await AssertStatus(403, () => fixture.Service.Create(Request(secondRestaurant, "POS-01"), default));
        Assert.Empty(fixture.Repository.Values);
    }

    [Fact]
    public async Task RevokedAssignmentIsCheckedAgainBeforeCreate()
    {
        var fixture = CreateFixture("manager", false, [firstRestaurant]);
        fixture.Access.TargetScope = Actor("manager", false, []);
        await AssertStatus(403, () => fixture.Service.Create(Request(firstRestaurant, "POS-01"), default));
        Assert.Empty(fixture.Repository.Values);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("POS\n01")]
    [InlineData("Quầy-01")]
    public async Task InvalidCodesDoNotCreateRegisters(string code)
    {
        var fixture = CreateFixture("manager", false, [firstRestaurant]);
        await AssertStatus(400, () => fixture.Service.Create(Request(firstRestaurant, code), default));
        Assert.Empty(fixture.Repository.Values);
    }

    [Fact]
    public async Task MissingRestaurantOrOverlengthCodeIsRejected()
    {
        var fixture = CreateFixture("admin", true, []);
        await AssertStatus(400, () => fixture.Service.Create(Request(Guid.Empty, "POS-01"), default));
        await AssertStatus(400, () => fixture.Service.Create(Request(firstRestaurant, new string('A', 51)), default));
    }

    [Fact]
    public async Task DuplicateCodeWithinBranchIsCaseInsensitiveButAnotherBranchCanUseIt()
    {
        var fixture = CreateFixture("admin", true, []);
        await fixture.Service.Create(Request(firstRestaurant, "POS-01"), default);
        await AssertStatus(409, () => fixture.Service.Create(Request(firstRestaurant, "pos-01"), default));
        await fixture.Service.Create(Request(secondRestaurant, "pos-01"), default);
        Assert.Equal(2, fixture.Repository.Values.Count);
    }

    [Fact]
    public async Task ContendedCreationReturnsConflictWithoutWriting()
    {
        var fixture = CreateFixture("admin", true, []);
        fixture.Repository.LockGranted = false;
        await AssertStatus(409, () => fixture.Service.Create(Request(firstRestaurant, "POS-01"), default));
        Assert.Empty(fixture.Repository.Values);
    }

    [Theory]
    [InlineData("manager")]
    [InlineData("cashier")]
    public async Task ListAndDetailAreLimitedToAssignedRestaurants(string role)
    {
        var fixture = CreateFixture(role, false, [firstRestaurant]);
        var own = Register(firstRestaurant, "OWN");
        var other = Register(secondRestaurant, "OTHER");
        fixture.Repository.Values.AddRange([own, other]);

        var page = await fixture.Service.GetAll(new CashRegisterQueryDTO(), default);
        Assert.Single(page.Items);
        Assert.Equal(own.CashRegisterId, page.Items[0].CashRegisterId);
        Assert.Equal(own.CashRegisterId, (await fixture.Service.GetById(own.CashRegisterId, default)).CashRegisterId);
        await AssertStatus(403, () => fixture.Service.GetById(other.CashRegisterId, default));
        await AssertStatus(403, () => fixture.Service.GetAll(new CashRegisterQueryDTO
        {
            RestaurantId = secondRestaurant
        }, default));
    }

    [Fact]
    public async Task AdministratorCanReadEveryRestaurantWithoutAnAssignment()
    {
        var fixture = CreateFixture("admin", true, []);
        fixture.Repository.Values.AddRange([Register(firstRestaurant, "A"), Register(secondRestaurant, "B")]);
        var result = await fixture.Service.GetAll(new CashRegisterQueryDTO(), default);
        Assert.Equal(2, result.TotalCount);
        Assert.Null(fixture.Repository.ReadScope);
    }

    [Theory]
    [InlineData("waiter", true)]
    [InlineData("cashier", false)]
    [InlineData("manager", false)]
    public async Task DisallowedRolesOrMissingAssignmentsCannotRead(string role, bool assigned)
    {
        var fixture = CreateFixture(role, false, assigned ? [firstRestaurant] : []);
        await AssertStatus(403, () => fixture.Service.GetAll(new CashRegisterQueryDTO(), default));
    }

    [Fact]
    public async Task MissingVerifiedUserIsUnauthorized()
    {
        var fixture = CreateFixture("cashier", false, [firstRestaurant]);
        fixture.Access.Scope.UserId = Guid.Empty;
        await AssertStatus(401, () => fixture.Service.GetAll(new CashRegisterQueryDTO(), default));
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task InvalidPaginationIsRejectedBeforeRepositoryRead(int page, int pageSize)
    {
        var fixture = CreateFixture("admin", true, []);
        await AssertStatus(400, () => fixture.Service.GetAll(new CashRegisterQueryDTO
        {
            Page = page,
            PageSize = pageSize
        }, default));
    }

    [Fact]
    public async Task MissingAndEmptyRegisterIdsReturnExpectedErrors()
    {
        var fixture = CreateFixture("cashier", false, [firstRestaurant]);
        await AssertStatus(400, () => fixture.Service.GetById(Guid.Empty, default));
        await AssertStatus(404, () => fixture.Service.GetById(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task EmptyConfigurationDoesNotGrantRolePermissions()
    {
        var fixture = CreateFixture("manager", false, [firstRestaurant], new CashRegisterAccessSettings());
        await AssertStatus(403, () => fixture.Service.GetAll(new CashRegisterQueryDTO(), default));
        await AssertStatus(403, () => fixture.Service.Create(Request(firstRestaurant, "A"), default));
    }

    private static async Task AssertStatus(int expected, Func<Task> action)
    {
        var error = await Assert.ThrowsAsync<BillingException>(action);
        Assert.Equal(expected, error.StatusCode);
    }

    private static CreateCashRegisterDTO Request(Guid restaurantId, string code)
    {
        return new CreateCashRegisterDTO
        {
            RestaurantId = restaurantId,
            Code = code
        };
    }

    private static CashRegister Register(Guid restaurantId, string code)
    {
        return new CashRegister
        {
            CashRegisterId = Guid.NewGuid(),
            RestaurantId = restaurantId,
            Code = code
        };
    }

    private static BillingAccessScope Actor(string role, bool administrator, IReadOnlyList<Guid> restaurants)
    {
        return new BillingAccessScope
        {
            UserId = Guid.NewGuid(),
            RoleId = role,
            IsAdministrator = administrator,
            RestaurantIds = restaurants
        };
    }

    private static Fixture CreateFixture(string role, bool administrator, IReadOnlyList<Guid> restaurants,
        CashRegisterAccessSettings? settings = null)
    {
        var repository = new FakeRepository();
        var unitOfWork = new FakeUnitOfWork();
        var access = new FakeAccess
        {
            Scope = Actor(role, administrator, restaurants)
        };
        var service = new CashRegisterService(repository, unitOfWork, access, Options.Create(settings ?? new CashRegisterAccessSettings
        {
            ManagerRoleIds = ["manager"],
            CashierRoleIds = ["cashier"]
        }));
        return new Fixture(service, repository, unitOfWork, access);
    }

    private sealed record Fixture(CashRegisterService Service, FakeRepository Repository, FakeUnitOfWork UnitOfWork, FakeAccess Access);

    private sealed class FakeAccess : IBillingAccess
    {
        public BillingAccessScope Scope { get; set; } = new();
        public BillingAccessScope? TargetScope { get; set; }
        public List<Guid?> RequestedRestaurants { get; } = [];

        public Task<BillingAccessScope> GetCurrentScopeAsync(Guid? restaurantId, CancellationToken cancellationToken)
        {
            RequestedRestaurants.Add(restaurantId);
            return Task.FromResult(restaurantId.HasValue ? TargetScope ?? Scope : Scope);
        }
    }

    private sealed class FakeRepository : ICashRegisterRepository
    {
        public Task<CashRegister?> LockByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            return GetByIdAsync(id, cancellationToken);
        }

        public List<CashRegister> Values { get; } = [];
        public Guid? LockedRestaurant { get; private set; }
        public bool LockGranted { get; set; } = true;
        public IReadOnlyList<Guid>? ReadScope { get; private set; }

        public Task<CashRegister?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(Values.SingleOrDefault(register => register.CashRegisterId == id));
        }

        public Task<PagedResult<CashRegister>> GetPageAsync(CashRegisterQueryDTO query, IReadOnlyList<Guid>? restaurantIds,
            CancellationToken cancellationToken)
        {
            ReadScope = restaurantIds;
            var filtered = Values.Where(register => restaurantIds is null || restaurantIds.Contains(register.RestaurantId))
                .Where(register => !query.RestaurantId.HasValue || register.RestaurantId == query.RestaurantId);
            var all = filtered.ToArray();
            return Task.FromResult(new PagedResult<CashRegister>(all, all.Length, query.Page, query.PageSize));
        }

        public Task<bool> CodeExistsAsync(Guid restaurantId, string code, CancellationToken cancellationToken)
        {
            return Task.FromResult(Values.Any(register => register.RestaurantId == restaurantId
                && register.Code.Equals(code, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<bool> LockRestaurantAsync(Guid restaurantId, CancellationToken cancellationToken)
        {
            LockedRestaurant = restaurantId;
            return Task.FromResult(LockGranted);
        }

        public Task<CashRegister> AddAsync(CashRegister register)
        {
            Values.Add(register);
            return Task.FromResult(register);
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int TransactionCount { get; private set; }

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
        {
            TransactionCount++;
            return action();
        }

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, IsolationLevel isolationLevel,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInTransactionAsync(action, cancellationToken);
        }

        public Task<int> SaveAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }

        public Task<bool> SaveChangeAsync()
        {
            return Task.FromResult(true);
        }

        public void Dispose()
        {
        }
    }
}
