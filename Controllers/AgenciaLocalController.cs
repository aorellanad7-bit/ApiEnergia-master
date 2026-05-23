using ApiEnergia.DTOs;
using ApiEnergia.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace ApiEnergia.Controllers
{
    /// <summary>
    /// Operaciones realizadas por personal de agencia (lecturas, alta de
    /// clientes, pagos en efectivo). Requieren autenticación.
    /// </summary>
    [ApiController]
    [Route("api/Energia/Agencia")]
    [Produces("application/json")]
    [Tags("Agencia Local")]
    [Authorize]
    public class AgenciaLocalController : ControllerBase
    {
        private readonly IEnergiaService _energiaService;
        private readonly IClientesService _clientesService;

        public AgenciaLocalController(IEnergiaService energiaService, IClientesService clientesService)
        {
            _energiaService = energiaService;
            _clientesService = clientesService;
        }

        [HttpPost("lectura")]
        [ProducesResponseType(typeof(ConsultarDeudaResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> RegistrarLectura([FromBody] RegistrarLecturaDto dto)
        {
            try
            {
                var recibo = await _energiaService.RegistrarLecturaAsync(dto.NumeroContador, dto.Kilovatios);
                return Ok(new ConsultarDeudaResponseDto(recibo.NumeroContador, recibo.SaldoPendiente));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { mensaje = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { mensaje = ex.Message });
            }
        }

        [HttpPost("cliente")]
        [ProducesResponseType(typeof(CrearClienteConContadorResponse), StatusCodes.Status201Created)]
        public async Task<IActionResult> CrearCliente([FromBody] CrearClienteConContadorRequest request)
        {
            var respuesta = await _clientesService.CrearClienteConContadorAsync(request);
            return Created(string.Empty, respuesta);
        }

        [HttpPost("pago-efectivo")]
        [ProducesResponseType(typeof(ResultadoPagoDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ResultadoPagoDto), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> PagoEfectivo([FromBody] PagoEfectivoAgenciaDto dto)
        {
            var resultado = await _energiaService.ProcesarPagoEfectivoAsync(dto.NumeroContador, dto.MontoRecibido);
            return resultado.Exito ? Ok(resultado) : BadRequest(resultado);
        }

        /// <summary>
        /// Permite a personal de agencia consultar la cuenta de cualquier
        /// cliente por DPI. Útil para atención al público.
        /// </summary>
        [HttpGet("cliente/{dpi}")]
        [ProducesResponseType(typeof(MiCuentaResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ConsultarCuentaCliente(string dpi)
        {
            var cuenta = await _energiaService.ObtenerCuentaPorDpiAsync(dpi);
            if (cuenta is null)
                return NotFound(new { mensaje = $"No se encontró cliente con DPI '{dpi}'." });
            return Ok(cuenta);
        }

        /// <summary>
        /// Lista los recibos de un cliente específico. Filtros opcionales por
        /// contador y estado.
        /// </summary>
        [HttpGet("cliente/{dpi}/recibos")]
        [ProducesResponseType(typeof(IReadOnlyList<ReciboResumenDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ListarRecibosCliente(
            string dpi,
            [FromQuery] string? numeroContador = null,
            [FromQuery] string? estado = null)
        {
            var recibos = await _energiaService.ListarRecibosPorDpiAsync(dpi, numeroContador, estado);
            return Ok(recibos);
        }
    }
}
