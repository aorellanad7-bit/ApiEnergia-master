using ApiEnergia.DbContext;
using ApiEnergia.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace ApiEnergia.Repositories
{
    public class Repository<T> : IRepository<T> where T : class
    {
        private readonly EnergiaDbContext _db;
        private readonly DbSet<T> _set;

        public Repository(EnergiaDbContext db)
        {
            _db = db;
            _set = db.Set<T>();
        }

        public async Task<T?> GetByIdAsync(object id)
        {
            return await _set.FindAsync(id);
        }

        public async Task<IReadOnlyList<T>> GetAllAsync()
        {
            return await _set.ToListAsync();
        }

        public IQueryable<T> Query()
        {
            return _set.AsQueryable();
        }

        public async Task AddAsync(T entity)
        {
            await _set.AddAsync(entity);
        }

        public void Update(T entity)
        {
            _set.Update(entity);
        }

        public void Remove(T entity)
        {
            _set.Remove(entity);
        }

        public async Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate)
        {
            return await _set.Where(predicate).ToListAsync();
        }
    }
}
