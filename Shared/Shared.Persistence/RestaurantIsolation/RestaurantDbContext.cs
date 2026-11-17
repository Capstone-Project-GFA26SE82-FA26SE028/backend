using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Shared.Persistence.RestaurantIsolation;

// API registrations supply a scope. The parameterless/options-only constructors are
// retained for EF tooling and existing isolated repository tests.
public abstract class RestaurantDbContext : DbContext
{
    private readonly RestaurantDataScope? restaurantScope;

    protected RestaurantDbContext()
    {
    }

    protected RestaurantDbContext(DbContextOptions options, RestaurantDataScope? scope = null) : base(options)
    {
        restaurantScope = scope;
    }

    public Guid? CurrentRestaurantId => restaurantScope?.RestaurantId;

    public bool RequiresRestaurantScope => restaurantScope is not null;

    protected virtual bool IsRestaurantEntity(Type type)
    {
        return true;
    }

    public static string OwnershipProperty(Type type)
    {
        return type.GetProperty("RestaurantRef") is not null ? "RestaurantRef" : "RestaurantId";
    }

    protected void ConfigureRestaurantIsolation(ModelBuilder builder)
    {
        foreach (var entity in builder.Model.GetEntityTypes().ToArray())
        {
            if (!IsRestaurantEntity(entity.ClrType))
            {
                continue;
            }

            var propertyName = OwnershipProperty(entity.ClrType);
            var propertyType = entity.ClrType.GetProperty(propertyName)?.PropertyType ?? typeof(Guid?);
            builder.Entity(entity.ClrType).Property(propertyType, propertyName);
            builder.Entity(entity.ClrType).HasIndex(propertyName);
            var parameter = Expression.Parameter(entity.ClrType, "row");
            var owner = Expression.Convert(Expression.Call(typeof(EF), nameof(EF.Property), [propertyType],
                parameter, Expression.Constant(propertyName)), typeof(Guid?));
            var context = Expression.Constant(this);
            var filter = Expression.OrElse(
                Expression.Not(Expression.Property(context, nameof(RequiresRestaurantScope))),
                Expression.AndAlso(
                    Expression.Property(Expression.Property(context, nameof(CurrentRestaurantId)), nameof(Nullable<Guid>.HasValue)),
                    Expression.Equal(owner, Expression.Property(context, nameof(CurrentRestaurantId)))));
            builder.Entity(entity.ClrType).HasQueryFilter(Expression.Lambda(filter, parameter));
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateOwnershipAsync(CancellationToken.None).GetAwaiter().GetResult();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await ValidateOwnershipAsync(cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private async Task ValidateOwnershipAsync(CancellationToken ct)
    {
        if (!RequiresRestaurantScope)
        {
            return;
        }

        var changed = ChangeTracker.Entries().Where(entry => IsRestaurantEntity(entry.Metadata.ClrType)
            && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        if (changed.Length == 0)
        {
            return;
        }

        var restaurantId = CurrentRestaurantId
            ?? throw new RestaurantScopeException("Vui lòng chọn nhà hàng trước khi thao tác dữ liệu.");
        foreach (var entry in changed)
        {
            var propertyName = OwnershipProperty(entry.Metadata.ClrType);
            var owner = entry.Property(propertyName);
            if (entry.State == EntityState.Added)
            {
                if (owner.CurrentValue is Guid supplied && supplied != Guid.Empty && supplied != restaurantId)
                {
                    throw new RestaurantScopeException("Dữ liệu không thuộc nhà hàng đang thao tác.");
                }

                owner.CurrentValue = restaurantId;
            }
            else
            {
                var persisted = await entry.GetDatabaseValuesAsync(ct);
                if (persisted is null || !Equals(persisted[propertyName], restaurantId)
                    || !Equals(owner.CurrentValue, restaurantId))
                {
                    throw new RestaurantScopeException("Không được sửa hoặc xóa dữ liệu của nhà hàng khác.");
                }
            }
        }

        foreach (var entry in changed.Where(value => value.State != EntityState.Deleted))
        {
            foreach (var foreignKey in entry.Metadata.GetForeignKeys())
            {
                if (!IsRestaurantEntity(foreignKey.PrincipalEntityType.ClrType))
                {
                    continue;
                }

                var values = foreignKey.Properties.Select(property => entry.Property(property.Name).CurrentValue).ToArray();
                if (values.Any(value => value is null))
                {
                    continue;
                }

                var principal = await FindAsync(foreignKey.PrincipalEntityType.ClrType, values, ct);
                if (principal is null
                    || !Equals(Entry(principal).Property(OwnershipProperty(foreignKey.PrincipalEntityType.ClrType)).CurrentValue, restaurantId))
                {
                    throw new RestaurantScopeException("Không được liên kết dữ liệu giữa các nhà hàng khác nhau.");
                }

                var principalEntry = Entry(principal);
                if (principalEntry.State != EntityState.Added)
                {
                    var persisted = await principalEntry.GetDatabaseValuesAsync(ct);
                    if (persisted is null || !Equals(persisted[OwnershipProperty(principalEntry.Metadata.ClrType)], restaurantId))
                    {
                        throw new RestaurantScopeException("Không được liên kết dữ liệu giữa các nhà hàng khác nhau.");
                    }
                }
            }
        }
    }
}
