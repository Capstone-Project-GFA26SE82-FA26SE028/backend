using Microsoft.EntityFrameworkCore;
using System.Data;
using Identity.DAL.Implements;
using Identity.DAL.Interfaces;
using Identity.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Identity.DAL.UnitOfWorks
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly IdentityDbContext _context;

        public UnitOfWork(IdentityDbContext context)
        {
            _context = context;
            UserRepo = new UserRepository(_context);
        }

        public IUserRepository UserRepo
        {
            get; private set;
        }

        public void Dispose()
        {
            _context.Dispose();
        }

        public void RecordAudit(Guid? actorId, string action, string entityName, string? entityId,
            object? oldValue = null, object? newValue = null)
        {
            _context.RecordAudit(actorId, action, entityName, entityId, oldValue, newValue);
        }
        public async Task<int> SaveAsync(CancellationToken cancellationToken = default)
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var result = await action();
            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }

        public async Task<bool> SaveChangeAsync()
        {
            return await _context.SaveChangesAsync() > 0;
        }

    }
}
