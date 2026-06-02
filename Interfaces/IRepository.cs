using System.Linq.Expressions;

namespace ApiEnergia.Interfaces
{
    public interface IRepository<T> where T : class
    {
        Task<T?> GetByIdAsync(object id);
        Task<IReadOnlyList<T>> GetAllAsync();
        IQueryable<T> Query();
        Task AddAsync(T entity);
        void Update(T entity);
        void Remove(T entity);
        Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate);

        /// <summary>
        /// Verifica si existe al menos una entidad que cumpla el predicado.
        /// Más eficiente que <c>(await FindAsync(...)).Any()</c> porque
        /// traduce a <c>SELECT EXISTS(...)</c> en MySQL en lugar de cargar
        /// todas las filas a memoria.
        /// </summary>
        Task<bool> AnyAsync(Expression<Func<T, bool>> predicate);

        /// <summary>
        /// Devuelve la primera entidad que cumpla el predicado o <c>null</c>.
        /// Equivalente a <c>(await FindAsync(...)).FirstOrDefault()</c> pero
        /// añade <c>LIMIT 1</c> en la consulta SQL.
        /// </summary>
        Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate);
    }
}
