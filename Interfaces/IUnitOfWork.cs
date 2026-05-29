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
        /// que escriben en varias tablas (alta de cliente + contador + usuario,
        /// aplicación de pago bancario con idempotencia). Llamar a
        /// <c>CommitAsync</c> al final del flujo para confirmar; en caso de
        /// excepción, el <c>using</c> hace rollback automático.
        /// </summary>
        Task<IDbContextTransaction> BeginTransactionAsync();
    }
}
