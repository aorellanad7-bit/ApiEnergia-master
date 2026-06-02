using ApiEnergia.Models;
using Microsoft.EntityFrameworkCore.Storage;

namespace ApiEnergia.Interfaces
{
    public interface IUnitOfWork
    {
        IRepository<ClienteLuz> Clientes { get; }
        IRepository<ContadorEnergia> Contadores { get; }
        IRepository<LecturaContador> Lecturas { get; }
        IRepository<ReciboLuz> Recibos { get; }
        IRepository<PagosProcesados> Pagos { get; }
        IRepository<UsuarioAccesoEnergia> Accesos { get; }
        Task<int> SaveChangesAsync();

        /// <summary>
        /// Inicia una transacción explícita. Necesaria para flujos multi-paso
        /// que escriben en varias tablas. <b>No usar directamente cuando el
        /// provider tiene <c>EnableRetryOnFailure</c> activo</b>: en ese caso
        /// llamar a <see cref="ExecuteInTransactionAsync(Func{Task})"/> en su
        /// lugar, que envuelve el bloque en la <c>IExecutionStrategy</c>
        /// configurada y soporta reintentos correctamente.
        /// </summary>
        Task<IDbContextTransaction> BeginTransactionAsync();

        /// <summary>
        /// Ejecuta <paramref name="operacion"/> dentro de una transacción
        /// explícita compatible con la estrategia de reintentos de Pomelo
        /// (<c>EnableRetryOnFailure</c>). Si la operación lanza, hace rollback;
        /// si retorna sin excepción, hace commit. La operación puede invocarse
        /// más de una vez si el provider decide reintentar, así que debe ser
        /// idempotente respecto al estado en memoria del DbContext.
        /// </summary>
        Task ExecuteInTransactionAsync(Func<Task> operacion);

        /// <summary>
        /// Variante con resultado de <see cref="ExecuteInTransactionAsync(Func{Task})"/>.
        /// </summary>
        Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operacion);
    }
}
