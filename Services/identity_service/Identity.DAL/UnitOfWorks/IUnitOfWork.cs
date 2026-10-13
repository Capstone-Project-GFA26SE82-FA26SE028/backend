using Identity.DAL.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Identity.DAL.UnitOfWorks
{
    public interface IUnitOfWork : Shared.Persistence.Interfaces.IUnitOfWork
    {
        void RecordAudit(Guid? actorId, string action, string entityName, string? entityId,
            object? oldValue = null, object? newValue = null);
        IUserRepository UserRepo
        {
            get;
        }
    }
}
