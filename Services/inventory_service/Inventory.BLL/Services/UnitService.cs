using Inventory.BLL.Interfaces;
using Inventory.Common;
using Inventory.DAL;
using Inventory.DAL.Models;
using Inventory.DAL.Repositories;

namespace Inventory.BLL.Services;

public sealed class UnitService(UnitRepository repository, InventoryTransaction transaction, IInventoryAccess access)
{
    public async Task<List<Unit>> GetAll(CancellationToken ct)
    {
        await access.RequireAsync("Read", ct);
        return await repository.GetAllAsync(ct);
    }

    public async Task<Unit> Create(string code, string name, CancellationToken ct)
    {
        await access.RequireAsync("ManageIngredients", ct);
        code = code.Trim().ToLowerInvariant();
        name = name.Trim();
        if (code.Length is < 1 or > 50 || code.Any(x => !char.IsAsciiLetterOrDigit(x) && x != '_')
            || name.Length is < 1 or > 100 || name.Any(char.IsControl))
        {
            throw new InventoryException(400, "Unit requires an ASCII code and a name of at most 100 characters.");
        }
        return await transaction.ExecuteAsync(async () =>
        {
            if (await repository.GetByCodeAsync(code, ct) is not null)
            {
                throw new InventoryException(409, "Unit code already exists.");
            }
            var unit = new Unit
            {
                UnitId = Guid.NewGuid(),
                Code = code,
                Name = name
            };
            await repository.CreateAsync(unit, ct);
            return unit;
        }, ct);
    }
}
