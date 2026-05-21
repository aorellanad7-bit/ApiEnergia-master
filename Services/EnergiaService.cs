using ApiEnergia.Interfaces;
using ApiEnergia.Models;

namespace ApiEnergia.Services
{
    public class EnergiaService : IEnergiaService
    {
        private readonly IUnitOfWork _unitOfWork;

        public EnergiaService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<ReciboLuz> RegistrarLecturaAsync(string numeroContador, int kilovatios)
        {
            if (string.IsNullOrWhiteSpace(numeroContador))
                throw new ArgumentException("NumeroContador es requerido.", nameof(numeroContador));

            if (kilovatios <= 0)
                throw new ArgumentOutOfRangeException(nameof(kilovatios), "Kilovatios debe ser mayor a 0.");

            bool contadorExiste = (await _unitOfWork.Contadores
                .FindAsync(t => t.NumeroContador == numeroContador)).Any();
            if (!contadorExiste)
                throw new InvalidOperationException($"El contador '{numeroContador}' no está registrado.");

            var lectura = new LecturaContador
            {
                NumeroContador = numeroContador,
                KilovatiosConsumidos = kilovatios,
                FechaLectura = DateTime.UtcNow
            };

            var monto = Math.Round(kilovatios * 1.50m, 2, MidpointRounding.AwayFromZero);
            var recibo = new ReciboLuz
            {
                NumeroContador = numeroContador,
                MontoTotal = monto,
                SaldoPendiente = monto,
                FechaEmision = DateTime.UtcNow,
                Estado = ReciboEstado.Pendiente,
                LecturaContador = lectura
            };

            await _unitOfWork.Lecturas.AddAsync(lectura);
            await _unitOfWork.Recibos.AddAsync(recibo);
            await _unitOfWork.SaveChangesAsync();

            return recibo;
        }

        public async Task<decimal> ConsultarDeudaTotalAsync(string numeroContador)
        {
            if (string.IsNullOrWhiteSpace(numeroContador))
                throw new ArgumentException("NumeroContador es requerido.", nameof(numeroContador));

            var recibos = await _unitOfWork.Recibos
                .FindAsync(r => r.NumeroContador == numeroContador && r.Estado == ReciboEstado.Pendiente);
            return recibos.Sum(r => r.SaldoPendiente);
        }

        public async Task ProcesarPagoExternoAsync(string numeroContador, decimal monto)
        {
            await ProcesarPagoAsync(numeroContador, monto, null);
        }

        public async Task ProcesarPagoEfectivoAsync(string numeroContador, decimal monto)
        {
            await ProcesarPagoAsync(numeroContador, monto, "EFECTIVO_AGENCIA");
        }

        private async Task ProcesarPagoAsync(string numeroContador, decimal monto, string? canal)
        {
            if (string.IsNullOrWhiteSpace(numeroContador))
                throw new ArgumentException("NumeroContador es requerido.", nameof(numeroContador));

            if (monto <= 0)
                throw new ArgumentOutOfRangeException(nameof(monto), "Monto debe ser mayor a 0.");

            var recibos = await _unitOfWork.Recibos
                .FindAsync(r => r.NumeroContador == numeroContador && r.Estado == ReciboEstado.Pendiente);
            var recibosOrdenados = recibos.OrderBy(r => r.FechaEmision).ToList();

            if (recibosOrdenados.Count == 0)
                return;

            var restante = monto;
            foreach (var recibo in recibosOrdenados)
            {
                if (restante <= 0)
                    break;

                var montoAplicado = Math.Min(restante, recibo.SaldoPendiente);

                if (restante >= recibo.SaldoPendiente)
                {
                    restante -= recibo.SaldoPendiente;
                    recibo.SaldoPendiente = 0;
                    recibo.Estado = ReciboEstado.Pagado;
                }
                else
                {
                    recibo.SaldoPendiente -= restante;
                    restante = 0;
                }
                
                if (canal is not null && montoAplicado > 0)
                {
                    await _unitOfWork.Pagos.AddAsync(new PagosProcesados
                    {
                        IdRecibo = recibo.IdRecibo,
                        NumeroContador = numeroContador,
                        Monto = montoAplicado,
                        FechaCobro = DateTime.UtcNow,
                        CanalPago = canal,
                        CodigoAutorizacionBanco = null
                    });
                }
            }

            await _unitOfWork.SaveChangesAsync();
        }
    }
}
