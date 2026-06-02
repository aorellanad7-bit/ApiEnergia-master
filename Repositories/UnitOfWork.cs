using ApiEnergia.DbContext;
using ApiEnergia.Interfaces;
using ApiEnergia.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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

        public Task<IDbContextTransaction> BeginTransactionAsync()
        {
            return _db.Database.BeginTransactionAsync();
        }

        public async Task ExecuteInTransactionAsync(Func<Task> operacion)
        {
            // CreateExecutionStrategy() devuelve la estrategia de reintentos
            // configurada en Program.cs (EnableRetryOnFailure). Toda la
            // transacción debe ejecutarse dentro de strategy.ExecuteAsync()
            // para que el provider la trate como una unidad retriable; de lo
            // contrario MySqlRetryingExecutionStrategy lanza
            //   "does not support user-initiated transactions".
            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var trx = await _db.Database.BeginTransactionAsync();
                await operacion();
                await trx.CommitAsync();
            });
        }

        public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operacion)
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var trx = await _db.Database.BeginTransactionAsync();
                var resultado = await operacion();
                await trx.CommitAsync();
                return resultado;
            });
        }
    }
}
