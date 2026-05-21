using ApiEnergia.DbContext;
using ApiEnergia.Interfaces;
using ApiEnergia.Models;

namespace ApiEnergia.Repositories
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly EnergiaDbContext _db;

        public UnitOfWork(EnergiaDbContext db)
        {
            _db = db;
            Clientes = new Repository<ClienteLuz>(_db);
            Contadores = new Repository<ContadorEnergia>(_db);
            Lecturas = new Repository<LecturaContador>(_db);
            Recibos = new Repository<ReciboLuz>(_db);
            Pagos = new Repository<PagosProcesados>(_db);
            Accesos = new Repository<UsuarioAccesoEnergia>(_db);
        }

        public IRepository<ClienteLuz> Clientes { get; }
        public IRepository<ContadorEnergia> Contadores { get; }
        public IRepository<LecturaContador> Lecturas { get; }
        public IRepository<ReciboLuz> Recibos { get; }
        public IRepository<PagosProcesados> Pagos { get; }
        public IRepository<UsuarioAccesoEnergia> Accesos { get; }

        public Task<int> SaveChangesAsync()
        {
            return _db.SaveChangesAsync();
        }
    }
}
