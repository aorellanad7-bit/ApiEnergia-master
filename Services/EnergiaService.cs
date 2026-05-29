using ApiEnergia.DTOs;
using ApiEnergia.Interfaces;
using ApiEnergia.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace ApiEnergia.Services
{
    public class EnergiaService : IEnergiaService
    {
        // Deben coincidir con el ENUM de la columna pagos_procesados.canal_pago
        private const string CanalBanco = "SISTEMA_BANCARIO";
        private const string CanalAgencia = "OFICINA_EMPRESA";

        // Tolerancia en quetzales para comparar montos `decimal`. Evita rechazar
        // un pago bancario por una diferencia de redondeo de medio centavo.
        private const decimal ToleranciaMonto = 0.005m;

        // Código de error de MySQL para "Duplicate entry" en un índice UNIQUE.
        // Lo aprovechamos para implementar idempotencia "optimista":
        // intentamos insertar el pago y, si la BD lo rechaza por colisión con
        // el UNIQUE compuesto (codigo_autorizacion_banco, id_recibo), sabemos
        // que el callback ya se aplicó previamente.
        private const int MySqlDuplicateKeyError = 1062;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IOptionsMonitor<TarifaOptions> _tarifa;

        public EnergiaService(IUnitOfWork unitOfWork, IOptionsMonitor<TarifaOptions> tarifa)
        {
            _unitOfWork = unitOfWork;
            // IOptionsMonitor (no IOptions) para que cambios en appsettings se
            // recojan en caliente sin reiniciar el App Service.
            _tarifa = tarifa;
        }

        public async Task<ReciboLuz> RegistrarLecturaAsync(string numeroContador, int kilovatios)
        {
            if (string.IsNullOrWhiteSpace(numeroContador))
                throw new ArgumentException("NumeroContador es requerido.", nameof(numeroContador));

            if (kilovatios <= 0)
                throw new ArgumentOutOfRangeException(nameof(kilovatios), "Kilovatios debe ser mayor a 0.");

            bool contadorExiste = await _unitOfWork.Contadores
                .AnyAsync(t => t.NumeroContador == numeroContador);
            if (!contadorExiste)
                throw new InvalidOperationException($"El contador '{numeroContador}' no está registrado.");

            var lectura = new LecturaContador
            {
                NumeroContador = numeroContador,
                KilovatiosConsumidos = kilovatios,
                FechaLectura = DateTime.UtcNow
            };

            // Tarifa configurable vía Tarifa:PrecioPorKwh en appsettings.
            // Antes estaba hardcoded a 1.50; ahora se puede ajustar sin redeploy.
            var precioPorKwh = _tarifa.CurrentValue.PrecioPorKwh;
            var monto = Math.Round(kilovatios * precioPorKwh, 2, MidpointRounding.AwayFromZero);
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
        /// Pago notificado por el Banco. Exige que el monto coincida con la deuda
        /// total pendiente (con tolerancia de medio centavo). Idempotente: si el
        /// banco reintenta el callback con la misma referencia, devolvemos un
        /// resultado <c>Exito=true</c> con <c>YaProcesado=true</c> en lugar de
        /// rechazarlo, para que el banco no marque el débito como fallido.
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
            bool contadorExiste = await _unitOfWork.Contadores
                .AnyAsync(c => c.NumeroContador == numeroContador);
            if (!contadorExiste)
                return new ResultadoPagoDto(false, $"El contador '{numeroContador}' no existe.", 0m, 0m, 0);

            // Idempotencia previa (rápida): si ya hay un pago con esta referencia,
            // contestamos éxito inmediatamente. La verificación final la garantiza
            // el UNIQUE (codigo_autorizacion_banco, id_recibo) en la BD; aquí solo
            // ahorramos el round-trip extra al INSERT cuando ya sabemos el resultado.
            if (!string.IsNullOrWhiteSpace(referenciaBanco))
            {
                bool yaAplicado = await _unitOfWork.Pagos
                    .AnyAsync(p => p.CodigoAutorizacionBanco == referenciaBanco);
                if (yaAplicado)
                {
                    var saldoRestante = await ConsultarDeudaTotalAsync(numeroContador);
                    return new ResultadoPagoDto(
                        Exito: true,
                        Mensaje: $"El pago con referencia '{referenciaBanco}' ya había sido aplicado.",
                        MontoAplicado: 0m,
                        SaldoRestante: saldoRestante,
                        RecibosAfectados: 0,
                        CambioADevolver: 0m,
                        YaProcesado: true);
                }
            }

            var recibos = await _unitOfWork.Recibos
                .FindAsync(r => r.NumeroContador == numeroContador && r.Estado == ReciboEstado.Pendiente);
            var recibosOrdenados = recibos.OrderBy(r => r.FechaEmision).ToList();

            if (recibosOrdenados.Count == 0)
                return new ResultadoPagoDto(false, "El contador no tiene deuda pendiente.", 0m, 0m, 0);

            var saldoTotal = recibosOrdenados.Sum(r => r.SaldoPendiente);

            // Comparación con tolerancia de medio centavo: el banco siempre paga
            // el saldo exacto que le devolvimos en /deuda, pero un redondeo
            // intermedio podría introducir 0.001 de diferencia.
            if (Math.Abs(monto - saldoTotal) > ToleranciaMonto)
            {
                return new ResultadoPagoDto(
                    false,
                    $"El monto debe coincidir con la deuda pendiente (Q{saldoTotal:N2}).",
                    0m, saldoTotal, recibosOrdenados.Count);
            }

            try
            {
                return await AplicarPagoAsync(
                    numeroContador,
                    saldoTotal, // usar exactamente el saldo conocido por la API
                    CanalBanco,
                    referenciaBanco,
                    recibosOrdenados);
            }
            catch (DbUpdateException ex) when (EsErrorDuplicateKey(ex))
            {
                // Carrera contra otro callback con la misma referencia: ya quedó
                // aplicado por el otro proceso. Reportamos éxito idempotente.
                var saldoRestante = await ConsultarDeudaTotalAsync(numeroContador);
                return new ResultadoPagoDto(
                    Exito: true,
                    Mensaje: $"El pago con referencia '{referenciaBanco}' ya había sido aplicado (carrera detectada).",
                    MontoAplicado: 0m,
                    SaldoRestante: saldoRestante,
                    RecibosAfectados: 0,
                    CambioADevolver: 0m,
                    YaProcesado: true);
            }
        }

        /// <summary>
        /// Pago en efectivo en agencia. Aplica FIFO sobre los recibos pendientes.
        /// Si el cajero recibe más efectivo del que cubre el saldo total, se aplica
        /// el saldo completo y el excedente se devuelve como <c>CambioADevolver</c>
        /// para que el cajero sepa cuánto entregar. Nunca queda dinero "huérfano"
        /// sin reportar.
        /// </summary>
        public async Task<ResultadoPagoDto> ProcesarPagoEfectivoAsync(string numeroContador, decimal monto)
        {
            if (string.IsNullOrWhiteSpace(numeroContador))
                return new ResultadoPagoDto(false, "NumeroContador es requerido.", 0m, 0m, 0);

            if (monto <= 0)
                return new ResultadoPagoDto(false, "El monto debe ser mayor a 0.", 0m, 0m, 0);

            bool contadorExiste = await _unitOfWork.Contadores
                .AnyAsync(c => c.NumeroContador == numeroContador);
            if (!contadorExiste)
                return new ResultadoPagoDto(false, $"El contador '{numeroContador}' no existe.", 0m, 0m, 0);

            var recibos = await _unitOfWork.Recibos
                .FindAsync(r => r.NumeroContador == numeroContador && r.Estado == ReciboEstado.Pendiente);
            var recibosOrdenados = recibos.OrderBy(r => r.FechaEmision).ToList();

            if (recibosOrdenados.Count == 0)
                return new ResultadoPagoDto(false, "El contador no tiene deuda pendiente.", 0m, 0m, 0);

            var saldoTotal = recibosOrdenados.Sum(r => r.SaldoPendiente);

            // Si el cliente entregó más de lo que debe, aplicamos solo el saldo y
            // el excedente queda como cambio. Esto reemplaza el comportamiento
            // anterior, que silenciosamente "se quedaba" con el sobrante.
            var aplicable = Math.Min(monto, saldoTotal);
            var cambio = monto - aplicable;

            var resultado = await AplicarPagoAsync(
                numeroContador,
                aplicable,
                CanalAgencia,
                null,
                recibosOrdenados);

            return resultado with
            {
                CambioADevolver = cambio,
                Mensaje = cambio > 0m
                    ? $"Pago aplicado correctamente. Devolver Q{cambio:N2} en efectivo al cliente."
                    : resultado.Mensaje
            };
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
                Mensaje: "Pago aplicado correctamente.",
                MontoAplicado: aplicado,
                SaldoRestante: saldoRestante,
                RecibosAfectados: afectados);
        }

        /// <summary>
        /// Detecta si un <see cref="DbUpdateException"/> fue causado por un
        /// duplicate key (error 1062 en MySQL). Lo usamos para implementar
        /// idempotencia: si el UNIQUE de pagos_procesados rechaza el INSERT,
        /// significa que el callback del banco ya se procesó previamente.
        /// </summary>
        private static bool EsErrorDuplicateKey(DbUpdateException ex)
        {
            // Pomelo expone la excepción nativa de MySql en InnerException.
            return ex.InnerException is MySqlException mySqlEx
                && mySqlEx.Number == MySqlDuplicateKeyError;
        }

        // ----------------------------------------------------------------
        // Consultas del portal de cliente
        // ----------------------------------------------------------------

        public async Task<MiCuentaResponseDto?> ObtenerCuentaPorDpiAsync(string dpi)
        {
            if (string.IsNullOrWhiteSpace(dpi)) return null;

            var cliente = await _unitOfWork.Clientes.FirstOrDefaultAsync(c => c.Dpi == dpi);
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

            var cliente = await _unitOfWork.Clientes.FirstOrDefaultAsync(c => c.Dpi == dpi);
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

            var cliente = await _unitOfWork.Clientes.FirstOrDefaultAsync(c => c.Dpi == dpi);
            if (cliente is null) return null;

            var recibo = await _unitOfWork.Recibos.FirstOrDefaultAsync(r => r.IdRecibo == idRecibo);
            if (recibo is null) return null;

            // Validar que el recibo pertenece a un contador del cliente.
            var perteneceAlCliente = await _unitOfWork.Contadores
                .AnyAsync(c => c.IdCliente == cliente.IdCliente && c.NumeroContador == recibo.NumeroContador);
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

            var cliente = await _unitOfWork.Clientes.FirstOrDefaultAsync(c => c.Dpi == dpi);
            if (cliente is null) return false;

            return await _unitOfWork.Contadores
                .AnyAsync(c => c.IdCliente == cliente.IdCliente && c.NumeroContador == numeroContador);
        }

        public Task<bool> ContadorExisteAsync(string numeroContador)
        {
            if (string.IsNullOrWhiteSpace(numeroContador))
                return Task.FromResult(false);

            return _unitOfWork.Contadores.AnyAsync(c => c.NumeroContador == numeroContador);
        }
    }
}
