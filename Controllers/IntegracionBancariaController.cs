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

        public IntegracionBancariaController(IEnergiaService energiaService)
        {
            _energiaService = energiaService;
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
                return Ok(new ConsultarDeudaResponseDto(numeroContador, saldo));
            }
            catch (ArgumentException ex)
            {
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
            var resultado = await _energiaService.ProcesarPagoExternoAsync(
                dto.NumeroContador,
                dto.Monto,
                dto.ReferenciaBanco);

            if (resultado.Exito)
                return Ok(resultado);

            // Si la causa es contador inexistente → 404, lo demás 400
            if ((resultado.Mensaje ?? string.Empty).Contains("no existe", StringComparison.OrdinalIgnoreCase))
                return NotFound(resultado);

            return BadRequest(resultado);
        }
    }
}
