using System.Globalization;
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

        public async Task<ReciboLuz> RegistrarLecturaAsync(
            string numeroContador, int kilovatios, int anio, int mes)
        {
            numeroContador = numeroContador?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(numeroContador))
                throw new ArgumentException("NumeroContador es requerido.", nameof(numeroContador));

            if (kilovatios <= 0)
                throw new ArgumentOutOfRangeException(nameof(kilovatios), "Kilovatios debe ser mayor a 0.");

            ValidarPeriodoLectura(anio, mes);

            bool contadorExiste = await _unitOfWork.Contadores
                .AnyAsync(t => t.NumeroContador == numeroContador);
            if (!contadorExiste)
                throw new InvalidOperationException($"El contador '{numeroContador}' no está registrado.");

            var disponible = await VerificarLecturaPeriodoDisponibleAsync(numeroContador, anio, mes);
            if (!disponible.Disponible)
                throw new InvalidOperationException(disponible.Mensaje
                    ?? $"Ya existe una lectura para el periodo {disponible.PeriodoEtiqueta}.");

            // Fecha de lectura = día 1 del periodo facturado (identifica el mes en BD).
            var fechaPeriodo = new DateTime(anio, mes, 1, 12, 0, 0, DateTimeKind.Utc);

            var lectura = new LecturaContador
            {
                NumeroContador = numeroContador,
                KilovatiosConsumidos = kilovatios,
                FechaLectura = fechaPeriodo,
                PeriodoAnio = anio,
                PeriodoMes = mes
            };

            var precioPorKwh = _tarifa.CurrentValue.PrecioPorKwh;
            var monto = Math.Round(kilovatios * precioPorKwh, 2, MidpointRounding.AwayFromZero);
            var recibo = new ReciboLuz
            {
                NumeroContador = numeroContador,
                MontoTotal = monto,
                SaldoPendiente = monto,
                FechaEmision = fechaPeriodo,
                Estado = ReciboEstado.Pendiente,
                LecturaContador = lectura
            };

            try
            {
                await _unitOfWork.Lecturas.AddAsync(lectura);
                await _unitOfWork.Recibos.AddAsync(recibo);
                await _unitOfWork.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (EsViolacionLecturaDuplicada(ex))
            {
                throw new InvalidOperationException(
                    $"Ya existe una lectura para el contador '{numeroContador}' en {EtiquetaPeriodo(anio, mes)}.");
            }

            return recibo;
        }

        public async Task<LecturaPeriodoDisponibleDto> VerificarLecturaPeriodoDisponibleAsync(
            string numeroContador, int anio, int mes)
        {
            numeroContador = numeroContador?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(numeroContador))
                throw new ArgumentException("NumeroContador es requerido.", nameof(numeroContador));

            ValidarPeriodoLectura(anio, mes);
            var etiqueta = EtiquetaPeriodo(anio, mes);

            bool contadorExiste = await _unitOfWork.Contadores
                .AnyAsync(t => t.NumeroContador == numeroContador);
            if (!contadorExiste)
            {
                return new LecturaPeriodoDisponibleDto(
                    Disponible: false,
                    PeriodoEtiqueta: etiqueta,
                    Mensaje: $"El contador '{numeroContador}' no está registrado.");
            }

            bool yaExiste = await ExisteLecturaEnPeriodoAsync(numeroContador, anio, mes);
            if (yaExiste)
            {
                return new LecturaPeriodoDisponibleDto(
                    Disponible: false,
                    PeriodoEtiqueta: etiqueta,
                    Mensaje: $"Ya se registró una lectura para el contador '{numeroContador}' en {etiqueta}.");
            }

            return new LecturaPeriodoDisponibleDto(
                Disponible: true,
                PeriodoEtiqueta: etiqueta,
                Mensaje: null);
        }

        private static void ValidarPeriodoLectura(int anio, int mes)
        {
            if (mes < 1 || mes > 12)
                throw new ArgumentException("El mes debe estar entre 1 y 12.");

            var hoy = DateTime.UtcNow;
            if (anio > hoy.Year || (anio == hoy.Year && mes > hoy.Month))
                throw new InvalidOperationException("No se puede registrar una lectura en un periodo futuro.");
        }

        private async Task<bool> ExisteLecturaEnPeriodoAsync(string numeroContador, int anio, int mes)
        {
            var inicioPeriodo = new DateTime(anio, mes, 1, 0, 0, 0, DateTimeKind.Utc);
            var finPeriodo = inicioPeriodo.AddMonths(1);

            bool enLecturas = await _unitOfWork.Lecturas.Query()
                .AnyAsync(l =>
                    l.NumeroContador == numeroContador
                    && (
                        (l.PeriodoAnio == anio && l.PeriodoMes == mes)
                        || (l.PeriodoAnio == 0 && l.FechaLectura >= inicioPeriodo && l.FechaLectura < finPeriodo)
                    ));

            if (enLecturas) return true;

            // Respaldo: recibo emitido en el mismo periodo (lecturas legacy sin periodo_anio/mes).
            return await _unitOfWork.Recibos.Query()
                .AnyAsync(r =>
                    r.NumeroContador == numeroContador
                    && r.FechaEmision >= inicioPeriodo
                    && r.FechaEmision < finPeriodo);
        }

        private static bool EsViolacionLecturaDuplicada(DbUpdateException ex)
        {
            for (var actual = ex.InnerException; actual != null; actual = actual.InnerException)
            {
                if (actual is MySqlException mysql && mysql.Number == 1062)
                {
                    var msg = mysql.Message ?? string.Empty;
                    return msg.Contains("ux_lectura_contador_periodo", StringComparison.OrdinalIgnoreCase)
                        || msg.Contains("numero_contador", StringComparison.OrdinalIgnoreCase);
                }
            }
            return false;
        }

        private static string EtiquetaPeriodo(int anio, int mes)
        {
            var cultura = CultureInfo.GetCultureInfo("es-GT");
            var nombreMes = cultura.DateTimeFormat.GetMonthName(mes);
            if (string.IsNullOrEmpty(nombreMes))
                return $"{mes:00}/{anio}";
            nombreMes = char.ToUpper(nombreMes[0], cultura) + nombreMes[1..];
            return $"{nombreMes} {anio}";
        }

        public async Task<decimal> ConsultarDeudaTotalAsync(string numeroContador)
        {
            if (string.IsNullOrWhiteSpace(numeroContador))
                throw new ArgumentException("NumeroContador es requerido.", nameof(numeroContador));

            var recibos = await _unitOfWork.Recibos
                .FindAsync(r => r.NumeroContador == numeroContador && r.Estado == ReciboEstado.Pendiente);
            return recibos.Sum(r => r.SaldoPendiente);
        }

        public async Task<ComprobantePagoBancoDto?> ObtenerComprobantePagoPorContadorAsync(string numeroContador)
        {
            if (string.IsNullOrWhiteSpace(numeroContador))
                throw new ArgumentException("NumeroContador es requerido.", nameof(numeroContador));

            var contadores = await _unitOfWork.Contadores
                .FindAsync(c => c.NumeroContador == numeroContador.Trim());
            var contador = contadores.FirstOrDefault();
            if (contador is null)
                return null;

            var cliente = await _unitOfWork.Clientes.GetByIdAsync(contador.IdCliente);
            if (cliente is null)
                return null;

            var recibosPendientes = await _unitOfWork.Recibos
                .FindAsync(r => r.NumeroContador == contador.NumeroContador && r.Estado == ReciboEstado.Pendiente);

            var cultura = new System.Globalization.CultureInfo("es-GT");
            var detalle = recibosPendientes
                .OrderBy(r => r.FechaEmision)
                .Select(r =>
                {
                    var periodo = r.FechaEmision.ToString("MMMM yyyy", cultura);
                    periodo = char.ToUpper(periodo[0]) + periodo[1..];
                    return new ComprobanteDetalleLineaDto(
                        $"Recibo #{r.IdRecibo} · {periodo}",
                        r.SaldoPendiente);
                })
                .ToList();

            var monto = detalle.Sum(d => d.Monto);

            return new ComprobantePagoBancoDto(
                NumeroContador: contador.NumeroContador,
                NombreCliente: cliente.Nombre,
                ApellidoCliente: cliente.Apellido,
                Dpi: cliente.Dpi,
                DireccionInmueble: contador.DireccionInmueble,
                MontoAPagar: monto,
                FechaEmision: DateTime.UtcNow,
                Detalle: detalle);
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

        /// <summary>
        /// Porcentaje de comisión que retiene el banco. Espejo de
        /// <c>API_Banco.Application.Common.DistribuidorPago95Por5.PorcentajeComision</c>.
        /// Si el banco cambia su regla, este número debe seguirlo o el dashboard
        /// de Energía dejará de cuadrar con el saldo de la cuenta prestadora.
        /// </summary>
        private const decimal PorcentajeComisionBanco = 0.05m;

        public async Task<TotalesRecaudacionDto> ObtenerTotalesRecaudacionAsync()
        {
            // Traemos solo lo necesario (canal, referencia, monto) para no
            // materializar entidades completas y evitar tracking innecesario.
            var pagos = _unitOfWork.Pagos
                .Query()
                .AsNoTracking()
                .Select(p => new
                {
                    p.CanalPago,
                    p.CodigoAutorizacionBanco,
                    p.Monto
                });
            var lista = await pagos.ToListAsync();

            // Pagos en efectivo de agencia: nunca pasan por el banco, así que
            // no afectan el saldo de la cuenta prestadora.
            var totalAgencia = lista
                .Where(p => p.CanalPago == CanalAgencia)
                .Sum(p => p.Monto);

            // Pagos bancarios: reconstruimos el monto ORIGINAL de cada cobro
            // del banco agrupando por la referencia bancaria. Un solo cobro
            // del banco puede aparecer aquí como varias filas (FIFO entre
            // recibos pendientes), pero la regla 95/5 se aplica sobre el
            // monto total tal como lo recibió el banco — no fila por fila.
            var pagosBanco = lista.Where(p => p.CanalPago == CanalBanco).ToList();

            var totalesPorCobroBancario = new List<decimal>();
            totalesPorCobroBancario.AddRange(pagosBanco
                .Where(p => !string.IsNullOrEmpty(p.CodigoAutorizacionBanco))
                .GroupBy(p => p.CodigoAutorizacionBanco!)
                .Select(g => g.Sum(p => p.Monto)));
            // Pagos bancarios sin referencia (datos legacy): los tratamos
            // como un cobro independiente cada uno para no agruparlos por error.
            totalesPorCobroBancario.AddRange(pagosBanco
                .Where(p => string.IsNullOrEmpty(p.CodigoAutorizacionBanco))
                .Select(p => p.Monto));

            decimal totalRecaudado = 0m;
            decimal totalComisiones = 0m;
            foreach (var montoCobro in totalesPorCobroBancario)
            {
                // Mismo redondeo que API_Banco.Application.Common.DistribuidorPago95Por5:
                // comision = round(monto * 0.05, 2, AwayFromZero); prestadora = monto - comision.
                var comision = Math.Round(
                    montoCobro * PorcentajeComisionBanco,
                    2,
                    MidpointRounding.AwayFromZero);
                totalRecaudado += montoCobro - comision;
                totalComisiones += comision;
            }

            var totalBancoBruto = totalesPorCobroBancario.Sum();
            var totalBruto = totalBancoBruto + totalAgencia;

            return new TotalesRecaudacionDto(
                TotalRecaudado: totalRecaudado,
                TotalCobradoBruto: totalBruto,
                TotalCobradoBanco: totalBancoBruto,
                TotalCobradoAgencia: totalAgencia,
                ComisionesBanco: totalComisiones,
                CantidadPagos: lista.Count);
        }
    }
}
