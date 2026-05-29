using ApiEnergia.Auth;
using ApiEnergia.DTOs;
using ApiEnergia.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ApiEnergia.Controllers
{
    /// <summary>
    /// Endpoints que consume EXCLUSIVAMENTE el API Banco para validar deuda y
    /// notificar pagos. Protegidos con API key compartido (header X-Api-Key).
    /// Las rutas están alineadas con el contrato que usa
    /// API_Banco.Infrastructure.Integrations.GestorIntegracionServicios.
    /// </summary>
    [ApiController]
    [Route("api/IntegracionBancaria")]
    [Produces("application/json")]
    [Tags("Integración Bancaria")]
    [RequiereApiKey]
    public class IntegracionBancariaController : ControllerBase
    {
        private readonly IEnergiaService _energiaService;
        private readonly ILogger<IntegracionBancariaController> _logger;

        public IntegracionBancariaController(
            IEnergiaService energiaService,
            ILogger<IntegracionBancariaController> logger)
        {
            _energiaService = energiaService;
            _logger = logger;
        }

        /// <summary>
        /// Devuelve el saldo total pendiente del contador. Lo invoca el banco
        /// antes de cobrar para validar el identificador y conocer la deuda.
        /// </summary>
        [HttpGet("deuda/{numeroContador}")]
        [ProducesResponseType(typeof(ConsultarDeudaResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ConsultarDeuda(string numeroContador)
        {
            if (string.IsNullOrWhiteSpace(numeroContador))
                return BadRequest(new { mensaje = "NumeroContador es requerido." });

            try
            {
                var saldo = await _energiaService.ConsultarDeudaTotalAsync(numeroContador);
                _logger.LogInformation(
                    "Consulta de deuda OK | contador={Contador} saldo={Saldo}",
                    numeroContador, saldo);
                return Ok(new ConsultarDeudaResponseDto(numeroContador, saldo));
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex,
                    "Consulta de deuda RECHAZADA | contador={Contador} motivo={Motivo}",
                    numeroContador, ex.Message);
                return BadRequest(new { mensaje = ex.Message });
            }
        }

        /// <summary>
        /// El banco notifica que el pago fue cobrado al cuentahabiente y
        /// pide a Energía marcar los recibos como pagados. El monto debe
        /// coincidir con la deuda total. Idempotente por ReferenciaBanco.
        /// </summary>
        [HttpPost("pago")]
        [ProducesResponseType(typeof(ResultadoPagoDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ResultadoPagoDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ResultadoPagoDto), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Pagar([FromBody] NotificacionPagoBancoDto dto)
        {
            // Logueamos la entrada COMPLETA del callback. Si algún día el
            // banco deja de llamar a este endpoint, basta con buscar este log
            // en Log Stream para confirmarlo. El X-Api-Key no se loguea por
            // seguridad, pero llegar aquí ya implica que pasó la validación.
            _logger.LogInformation(
                "Callback Banco RECIBIDO | contador={Contador} monto={Monto} referencia={Referencia}",
                dto?.NumeroContador, dto?.Monto, dto?.ReferenciaBanco);

            if (dto is null)
            {
                _logger.LogWarning("Callback Banco RECHAZADO | body nulo");
                return BadRequest(new ResultadoPagoDto(false, "Body de la notificación es requerido.", 0m, 0m, 0));
            }

            ResultadoPagoDto resultado;
            try
            {
                resultado = await _energiaService.ProcesarPagoExternoAsync(
                    dto.NumeroContador,
                    dto.Monto,
                    dto.ReferenciaBanco);
            }
            catch (Exception ex)
            {
                // Si la BD truena, el banco recibe 500 y NO debe marcar el
                // débito como notificado, así puede reintentar.
                _logger.LogError(ex,
                    "Callback Banco EXPLOTÓ | contador={Contador} monto={Monto} referencia={Referencia}",
                    dto.NumeroContador, dto.Monto, dto.ReferenciaBanco);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new ResultadoPagoDto(false, $"Error interno aplicando el pago: {ex.Message}", 0m, 0m, 0));
            }

            if (resultado.Exito)
            {
                if (resultado.YaProcesado)
                {
                    // Idempotencia: el banco reintentó un callback que ya
                    // habíamos aplicado. Respondemos 200 OK para que el banco
                    // marque el débito como notificado y deje de reintentar,
                    // pero lo logueamos distinto para no confundirlo con una
                    // aplicación nueva en métricas.
                    _logger.LogInformation(
                        "Callback Banco IDEMPOTENTE | contador={Contador} referencia={Referencia} (ya estaba aplicado)",
                        dto.NumeroContador, dto.ReferenciaBanco);
                }
                else
                {
                    _logger.LogInformation(
                        "Callback Banco APLICADO | contador={Contador} aplicado={Aplicado} saldoRestante={Saldo} recibosAfectados={Recibos}",
                        dto.NumeroContador, resultado.MontoAplicado, resultado.SaldoRestante, resultado.RecibosAfectados);
                }
                return Ok(resultado);
            }

            _logger.LogWarning(
                "Callback Banco RECHAZADO | contador={Contador} monto={Monto} motivo={Motivo}",
                dto.NumeroContador, dto.Monto, resultado.Mensaje);

            // Mapear contador inexistente a 404 vs el resto de errores a 400.
            // Antes hacíamos string-match contra "no existe" en el mensaje, lo
            // cual era frágil; ahora consultamos el repositorio directamente.
            var contadorExiste = await _energiaService.ContadorExisteAsync(dto.NumeroContador);
            if (!contadorExiste)
                return NotFound(resultado);

            return BadRequest(resultado);
        }
    }
}
