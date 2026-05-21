using ApiEnergia.Models;

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
    }
}
