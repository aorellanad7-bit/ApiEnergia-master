using ApiEnergia.DTOs;
using ApiEnergia.Interfaces;
using ApiEnergia.Models;

namespace ApiEnergia.Services
{
    public class EnergiaService : IEnergiaService
    {
        // Deben coincidir con el ENUM de la columna pagos_procesados.canal_pago
        private const string CanalBanco = "SISTEMA_BANCARIO";
        private const string CanalAgencia = "OFICINA_EMPRESA";

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

        /// <summary>
        /// Pago notificado por el Banco. Exige que el monto coincida exactamente con
        /// la deuda total pendiente (no se aceptan parciales para evitar dejar saldo huérfano).
        /// </summary>
        public async Task<ResultadoPagoDto> ProcesarPagoExternoAsync(
            string numeroContador,
            decimal monto,
            string? referenciaBanco = null)
        {
            if (string.IsNullOrWhiteSpace(numeroContador))
                return new ResultadoPagoDto(false, "NumeroContador es requerido.", 0m, 0m, 0);

            if (monto <= 0)
                return new ResultadoPagoDto(false, "El monto debe ser mayor a 0.", 0m, 0m, 0);

            // Validar que el contador exista
            bool contadorExiste = (await _unitOfWork.Contadores
                .FindAsync(c => c.NumeroContador == numeroContador)).Any();
            if (!contadorExiste)
                return new ResultadoPagoDto(false, $"El contador '{numeroContador}' no existe.", 0m, 0m, 0);

            // Idempotencia: si ya existe un pago con la misma referencia bancaria, evitamos duplicado
            if (!string.IsNullOrWhiteSpace(referenciaBanco))
            {
                var duplicado = await _unitOfWork.Pagos
                    .FindAsync(p => p.CodigoAutorizacionBanco == referenciaBanco);
                if (duplicado.Any())
                {
                    return new ResultadoPagoDto(
                        false,
                        $"Ya existe un pago registrado con la referencia bancaria '{referenciaBanco}'.",
                        0m, 0m, 0);
                }
            }

            var recibos = await _unitOfWork.Recibos
                .FindAsync(r => r.NumeroContador == numeroContador && r.Estado == ReciboEstado.Pendiente);
            var recibosOrdenados = recibos.OrderBy(r => r.FechaEmision).ToList();

            if (recibosOrdenados.Count == 0)
                return new ResultadoPagoDto(false, "El contador no tiene deuda pendiente.", 0m, 0m, 0);

            var saldoTotal = recibosOrdenados.Sum(r => r.SaldoPendiente);

            // El banco siempre paga el saldo total exacto (lo conoce porque consulta primero)
            if (monto != saldoTotal)
            {
                return new ResultadoPagoDto(
                    false,
                    $"El monto debe coincidir con la deuda pendiente (Q{saldoTotal:N2}).",
                    0m, saldoTotal, recibosOrdenados.Count);
            }

            return await AplicarPagoAsync(
                numeroContador,
                monto,
                CanalBanco,
                referenciaBanco,
                recibosOrdenados);
        }

        /// <summary>
        /// Pago en efectivo en agencia. Permite pagos parciales aplicados FIFO.
        /// </summary>
        public async Task<ResultadoPagoDto> ProcesarPagoEfectivoAsync(string numeroContador, decimal monto)
        {
            if (string.IsNullOrWhiteSpace(numeroContador))
                return new ResultadoPagoDto(false, "NumeroContador es requerido.", 0m, 0m, 0);

            if (monto <= 0)
                return new ResultadoPagoDto(false, "El monto debe ser mayor a 0.", 0m, 0m, 0);

            bool contadorExiste = (await _unitOfWork.Contadores
                .FindAsync(c => c.NumeroContador == numeroContador)).Any();
            if (!contadorExiste)
                return new ResultadoPagoDto(false, $"El contador '{numeroContador}' no existe.", 0m, 0m, 0);

            var recibos = await _unitOfWork.Recibos
                .FindAsync(r => r.NumeroContador == numeroContador && r.Estado == ReciboEstado.Pendiente);
            var recibosOrdenados = recibos.OrderBy(r => r.FechaEmision).ToList();

            if (recibosOrdenados.Count == 0)
                return new ResultadoPagoDto(false, "El contador no tiene deuda pendiente.", 0m, 0m, 0);

            return await AplicarPagoAsync(
                numeroContador,
                monto,
                CanalAgencia,
                null,
                recibosOrdenados);
        }

        // ----------------------------------------------------------------
        // Aplicación común: distribuye el monto FIFO sobre los recibos
        // pendientes, registra cada porción en pagos_procesados y guarda.
        // ----------------------------------------------------------------
        private async Task<ResultadoPagoDto> AplicarPagoAsync(
            string numeroContador,
            decimal monto,
            string canal,
            string? referenciaBanco,
            List<ReciboLuz> recibosPendientes)
        {
            var restante = monto;
            var aplicado = 0m;
            var afectados = 0;

            foreach (var recibo in recibosPendientes)
            {
                if (restante <= 0) break;

                var aPagar = Math.Min(restante, recibo.SaldoPendiente);

                recibo.SaldoPendiente -= aPagar;
                if (recibo.SaldoPendiente == 0)
                    recibo.Estado = ReciboEstado.Pagado;

                await _unitOfWork.Pagos.AddAsync(new PagosProcesados
                {
                    IdRecibo = recibo.IdRecibo,
                    NumeroContador = numeroContador,
                    Monto = aPagar,
                    FechaCobro = DateTime.UtcNow,
                    CanalPago = canal,
                    CodigoAutorizacionBanco = referenciaBanco
                });

                restante -= aPagar;
                aplicado += aPagar;
                afectados++;
            }

            await _unitOfWork.SaveChangesAsync();

            var saldoRestante = recibosPendientes.Sum(r => r.SaldoPendiente);

            return new ResultadoPagoDto(
                Exito: true,
                Mensaje: aplicado == monto
                    ? "Pago aplicado correctamente."
                    : "Pago aplicado parcialmente (sobró efectivo no aplicable).",
                MontoAplicado: aplicado,
                SaldoRestante: saldoRestante,
                RecibosAfectados: afectados);
        }

        // ----------------------------------------------------------------
        // Consultas del portal de cliente
        // ----------------------------------------------------------------

        public async Task<MiCuentaResponseDto?> ObtenerCuentaPorDpiAsync(string dpi)
        {
            if (string.IsNullOrWhiteSpace(dpi)) return null;

            var cliente = (await _unitOfWork.Clientes.FindAsync(c => c.Dpi == dpi)).FirstOrDefault();
            if (cliente is null) return null;

            var contadores = await _unitOfWork.Contadores
                .FindAsync(c => c.IdCliente == cliente.IdCliente);
            var numerosContador = contadores.Select(c => c.NumeroContador).ToList();

            // Cargar todos los recibos pendientes de los contadores del cliente
            // de una sola vez para no hacer N+1 queries.
            var recibosPendientes = await _unitOfWork.Recibos
                .FindAsync(r => numerosContador.Contains(r.NumeroContador) && r.Estado == ReciboEstado.Pendiente);

            var saldosPorContador = recibosPendientes
                .GroupBy(r => r.NumeroContador)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.SaldoPendiente));

            var contadoresDto = contadores
                .OrderBy(c => c.FechaInstalacion)
                .Select(c => new ContadorResumenDto(
                    NumeroContador: c.NumeroContador,
                    DireccionInmueble: c.DireccionInmueble,
                    Estado: c.Estado,
                    FechaInstalacion: c.FechaInstalacion,
                    SaldoPendiente: saldosPorContador.GetValueOrDefault(c.NumeroContador, 0m)))
                .ToList();

            var saldoTotal = contadoresDto.Sum(c => c.SaldoPendiente);

            return new MiCuentaResponseDto(
                IdCliente: cliente.IdCliente,
                Dpi: cliente.Dpi,
                Nombre: cliente.Nombre,
                Apellido: cliente.Apellido,
                Correo: cliente.Correo,
                SaldoTotalPendiente: saldoTotal,
                Contadores: contadoresDto);
        }

        public async Task<IReadOnlyList<ReciboResumenDto>> ListarRecibosPorDpiAsync(
            string dpi,
            string? numeroContador = null,
            string? estado = null)
        {
            if (string.IsNullOrWhiteSpace(dpi)) return Array.Empty<ReciboResumenDto>();

            var cliente = (await _unitOfWork.Clientes.FindAsync(c => c.Dpi == dpi)).FirstOrDefault();
            if (cliente is null) return Array.Empty<ReciboResumenDto>();

            var contadoresCliente = await _unitOfWork.Contadores
                .FindAsync(c => c.IdCliente == cliente.IdCliente);
            var numerosContador = contadoresCliente.Select(c => c.NumeroContador).ToHashSet();

            // Si pasaron numeroContador, asegurar que pertenezca al cliente.
            if (!string.IsNullOrWhiteSpace(numeroContador))
            {
                if (!numerosContador.Contains(numeroContador))
                    return Array.Empty<ReciboResumenDto>();
                numerosContador = new HashSet<string> { numeroContador };
            }

            var recibos = await _unitOfWork.Recibos
                .FindAsync(r => numerosContador.Contains(r.NumeroContador));

            // Filtro de estado opcional, case-insensitive
            IEnumerable<ReciboLuz> filtrados = recibos;
            if (!string.IsNullOrWhiteSpace(estado)
                && Enum.TryParse<ReciboEstado>(estado, ignoreCase: true, out var estadoEnum))
            {
                filtrados = filtrados.Where(r => r.Estado == estadoEnum);
            }

            return filtrados
                .OrderByDescending(r => r.FechaEmision)
                .ThenByDescending(r => r.IdRecibo)
                .Select(r => new ReciboResumenDto(
                    IdRecibo: r.IdRecibo,
                    NumeroContador: r.NumeroContador,
                    FechaEmision: r.FechaEmision,
                    MontoTotal: r.MontoTotal,
                    SaldoPendiente: r.SaldoPendiente,
                    Estado: r.Estado.ToString()))
                .ToList();
        }

        public async Task<IReadOnlyList<PagoResumenDto>?> ListarPagosDeReciboAsync(string dpi, int idRecibo)
        {
            if (string.IsNullOrWhiteSpace(dpi) || idRecibo <= 0) return null;

            var cliente = (await _unitOfWork.Clientes.FindAsync(c => c.Dpi == dpi)).FirstOrDefault();
            if (cliente is null) return null;

            var recibo = (await _unitOfWork.Recibos.FindAsync(r => r.IdRecibo == idRecibo)).FirstOrDefault();
            if (recibo is null) return null;

            // Validar que el recibo pertenece a un contador del cliente.
            var perteneceAlCliente = (await _unitOfWork.Contadores
                .FindAsync(c => c.IdCliente == cliente.IdCliente && c.NumeroContador == recibo.NumeroContador))
                .Any();
            if (!perteneceAlCliente) return null;

            var pagos = await _unitOfWork.Pagos.FindAsync(p => p.IdRecibo == idRecibo);

            return pagos
                .OrderByDescending(p => p.FechaCobro)
                .Select(p => new PagoResumenDto(
                    IdPago: p.IdPago,
                    IdRecibo: p.IdRecibo,
                    NumeroContador: p.NumeroContador,
                    Monto: p.Monto,
                    FechaCobro: p.FechaCobro,
                    CanalPago: p.CanalPago,
                    CodigoAutorizacionBanco: p.CodigoAutorizacionBanco))
                .ToList();
        }

        public async Task<bool> ContadorPerteneceAClienteAsync(string dpi, string numeroContador)
        {
            if (string.IsNullOrWhiteSpace(dpi) || string.IsNullOrWhiteSpace(numeroContador))
                return false;

            var cliente = (await _unitOfWork.Clientes.FindAsync(c => c.Dpi == dpi)).FirstOrDefault();
            if (cliente is null) return false;

            return (await _unitOfWork.Contadores
                .FindAsync(c => c.IdCliente == cliente.IdCliente && c.NumeroContador == numeroContador))
                .Any();
        }
    }
}
