using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Persistence.Interfaces
{
    public interface IGenericRepository<T> where T : class
    {
        T GetById(string id);
        T GetByGuid(Guid id);
        Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        IQueryable<T> GetAll();
        IQueryable<T> FindAll(Expression<Func<T, bool>> expression);
        Task<List<T>> FindAllAsync(Expression<Func<T, bool>> expression);
        Task<bool> AnyAsync(Expression<Func<T, bool>> expression, CancellationToken cancellationToken = default);
        Task<T> AddAsync(T entity);
        T Add(T entity);
        Task<T> GetByIdsAsync(int id);
        Task<T> UpdateAsync(T entity);
        bool Delete(T entity);
        Task<List<T>> GetAllByListAsync(Expression<Func<T, bool>> expression);
        Task<List<T>> GetAllByListAsync(Expression<Func<T, bool>> expression, Func<IQueryable<T>, IQueryable<T>> include);
        void UpdateRange(List<T> entity);
        void RemoveRange(List<T> entity);
        void AddRange(List<T> entity);
        Task DeleteAsync(Guid id);
        Task<T> FirstOrDefaultAsync(Expression<Func<T, bool>> expression);
        Task<T> GetByConditionAsync(Expression<Func<T, bool>> expression);
        Task<List<T>> ToListAsync();
    }

}
