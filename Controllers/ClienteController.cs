using ApiEnergia.DTOs;
using ApiEnergia.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Text.Json;

namespace ApiEnergia.Controllers
{
    /// <summary>
    /// Endpoints del portal del cliente. Todos requieren JWT con rol CLIENTE.
    /// La identidad del cliente se obtiene de <c>User.Identity.Name</c>, que
    /// contiene el DPI (= nombre_usuario en usuario_acceso_energia).
    /// </summary>
    [ApiController]
    [Route("api/Energia/Cliente")]
    [Produces("application/json")]
    [Tags("Cliente (Portal)")]
    [Authorize(Roles = "CLIENTE")]
    public class ClienteController : ControllerBase
    {
        // TipoServicioPublico.EnergiaElectrica = 3 (contrato del API Banco).
        // Se manda como entero para coincidir con lo que ya hace UMG y para no
        // acoplar este proyecto al ensamblado del banco.
        private const int TipoServicioEnergiaElectrica = 3;

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly IEnergiaService _energiaService;
        private readonly IHttpClientFactory _httpClientFactory;

        public ClienteController(IEnergiaService energiaService, IHttpClientFactory httpClientFactory)
        {
            _energiaService = energiaService;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Resumen de la cuenta del cliente autenticado: datos personales,
        /// contadores asociados con su saldo pendiente y saldo total.
        /// </summary>
        [HttpGet("mi-cuenta")]
        [ProducesResponseType(typeof(MiCuentaResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> MiCuenta()
        {
            var dpi = User.Identity?.Name;
            if (string.IsNullOrEmpty(dpi))
                return Unauthorized(new { mensaje = "Token sin identidad." });

            var cuenta = await _energiaService.ObtenerCuentaPorDpiAsync(dpi);
            if (cuenta is null)
                return NotFound(new { mensaje = "Cliente no encontrado." });

            return Ok(cuenta);
        }

        /// <summary>
        /// Lista los recibos del cliente. Filtros opcionales:
        /// <c>numeroContador</c> (de los que ya tiene asignados) y
        /// <c>estado</c> (Pendiente, Pagado, Vencido).
        /// </summary>
        [HttpGet("recibos")]
        [ProducesResponseType(typeof(IReadOnlyList<ReciboResumenDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> ListarRecibos(
            [FromQuery] string? numeroContador = null,
            [FromQuery] string? estado = null)
        {
            var dpi = User.Identity?.Name;
            if (string.IsNullOrEmpty(dpi))
                return Unauthorized(new { mensaje = "Token sin identidad." });

            var recibos = await _energiaService.ListarRecibosPorDpiAsync(dpi, numeroContador, estado);
            return Ok(recibos);
        }

        /// <summary>
        /// Detalle de pagos aplicados a un recibo. Solo retorna el detalle si
        /// el recibo pertenece a un contador del cliente autenticado; en caso
        /// contrario devuelve 404 (sin distinguir "no existe" de "no es tuyo"
        /// para no filtrar información a otros clientes).
        /// </summary>
        [HttpGet("recibos/{idRecibo:int}/pagos")]
        [ProducesResponseType(typeof(IReadOnlyList<PagoResumenDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ListarPagosDeRecibo(int idRecibo)
        {
            var dpi = User.Identity?.Name;
            if (string.IsNullOrEmpty(dpi))
                return Unauthorized(new { mensaje = "Token sin identidad." });

            var pagos = await _energiaService.ListarPagosDeReciboAsync(dpi, idRecibo);
            if (pagos is null)
                return NotFound(new { mensaje = "Recibo no encontrado o no pertenece al cliente." });

            return Ok(pagos);
        }

        /// <summary>
        /// Paga el saldo TOTAL del contador desde el portal usando la tarjeta del cliente.
        /// Orquesta el cobro contra el API Banco igual que el endpoint /pagar de UMG.
        /// El banco, tras debitar la cuenta, llamará a /api/IntegracionBancaria/pago
        /// para que Energía marque los recibos como pagados (flujo ya existente).
        /// </summary>
        /// <remarks>
        /// Reglas:
        /// 1) El contador debe pertenecer al cliente autenticado (DPI del JWT).
        /// 2) El monto lo calcula Energía con el saldo pendiente actual; no se acepta
        ///    monto del cliente para evitar manipulación desde el frontend.
        /// 3) Si el banco responde error, NO se modifica nada local: el banco solo
        ///    notifica el éxito a /api/IntegracionBancaria/pago.
        /// </remarks>
        [HttpPost("pagar")]
        [ProducesResponseType(typeof(PagarSaldoClienteResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> Pagar(
            [FromBody] PagarSaldoClienteDto request,
            CancellationToken cancellationToken)
        {
            var dpi = User.Identity?.Name;
            if (string.IsNullOrEmpty(dpi))
                return Unauthorized(new { mensaje = "Token sin identidad." });

            if (request is null)
                return BadRequest(new { mensaje = "Cuerpo de la petición vacío." });

            if (string.IsNullOrWhiteSpace(request.NumeroContador))
                return BadRequest(new { mensaje = "El número de contador es obligatorio." });

            if (string.IsNullOrWhiteSpace(request.NumeroTarjeta))
                return BadRequest(new { mensaje = "El número de tarjeta es obligatorio." });

            if (string.IsNullOrWhiteSpace(request.Pin))
                return BadRequest(new { mensaje = "El PIN es obligatorio." });

            var numeroContador = request.NumeroContador.Trim();

            // 1) Validar pertenencia del contador al cliente autenticado
            var perteneceAlCliente = await _energiaService.ContadorPerteneceAClienteAsync(dpi, numeroContador);
            if (!perteneceAlCliente)
                return Forbid();

            // 2) Calcular el saldo del lado del servidor — el cliente NO lo manda
            decimal saldoPendiente;
            try
            {
                saldoPendiente = await _energiaService.ConsultarDeudaTotalAsync(numeroContador);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { mensaje = ex.Message });
            }

            if (saldoPendiente <= 0m)
                return BadRequest(new { mensaje = "El contador no tiene saldo pendiente." });

            // 3) Construir y enviar la solicitud al API Banco (mismo contrato que UMG)
            var payload = new
            {
                numeroTarjeta = request.NumeroTarjeta.Trim(),
                pin = request.Pin.Trim(),
                tipoServicio = TipoServicioEnergiaElectrica,
                identificador = numeroContador,
                monto = saldoPendiente,
                referenciaCliente = string.IsNullOrWhiteSpace(request.ReferenciaCliente)
                    ? $"Pago Energía {numeroContador}"
                    : request.ReferenciaCliente.Trim()
            };

            HttpResponseMessage response;
            string rawBody;
            try
            {
                var client = _httpClientFactory.CreateClient("BancoApi");
                response = await client
                    .PostAsJsonAsync("api/Pagos/ejecutar", payload, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);

                rawBody = await response.Content
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    mensaje = "No se pudo contactar al API Banco.",
                    error = ex.Message
                });
            }

            object? respuestaBanco = null;
            if (!string.IsNullOrWhiteSpace(rawBody))
            {
                try
                {
                    respuestaBanco = JsonSerializer.Deserialize<JsonElement>(rawBody, JsonOptions);
                }
                catch (JsonException)
                {
                    respuestaBanco = rawBody;
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode, new
                {
                    mensaje = "El banco rechazó el pago.",
                    statusBanco = (int)response.StatusCode,
                    respuestaBanco = respuestaBanco ?? "(respuesta vacía del banco)"
                });
            }

            // El callback del banco /api/IntegracionBancaria/pago ya pone el saldo en 0,
            // pero lo consultamos de nuevo para devolver al frontend el estado actual.
            var saldoRestante = await _energiaService.ConsultarDeudaTotalAsync(numeroContador);

            return Ok(new PagarSaldoClienteResponseDto(
                NumeroContador: numeroContador,
                MontoPagado: saldoPendiente,
                SaldoRestante: saldoRestante,
                Mensaje: "Pago procesado con éxito.",
                RespuestaBanco: respuestaBanco));
        }
    }
}
