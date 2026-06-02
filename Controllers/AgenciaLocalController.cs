using ApiEnergia.DTOs;
using ApiEnergia.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace ApiEnergia.Controllers
{
    /// <summary>
    /// Operaciones realizadas por personal de agencia (lecturas, alta de
    /// clientes, pagos en efectivo). Restringidas al rol <c>ADMIN_AGENCIA</c>:
    /// los usuarios con rol <c>CLIENTE</c> no pueden invocar estos endpoints
    /// (de lo contrario podrían crear clientes, registrar lecturas o procesar
    /// pagos de cualquier contador).
    /// </summary>
    [ApiController]
    [Route("api/Energia/Agencia")]
    [Produces("application/json")]
    [Tags("Agencia Local")]
    [Authorize(Roles = "ADMIN_AGENCIA")]
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
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> CrearCliente([FromBody] CrearClienteConContadorRequest request)
        {
            try
            {
                var respuesta = await _clientesService.CrearClienteConContadorAsync(request);
                // 201 + Location apunta al endpoint de consulta del cliente recién creado.
                return CreatedAtAction(
                    nameof(ConsultarCuentaCliente),
                    new { dpi = respuesta.UsuarioAsignado },
                    respuesta);
            }
            catch (ArgumentNullException ex)
            {
                return BadRequest(new { mensaje = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                // Ej.: ya existe un usuario_acceso_energia con ese DPI.
                return Conflict(new { mensaje = ex.Message });
            }
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
        /// Genera los datos del comprobante de pago en ventanilla bancaria.
        /// El número de comprobante es el número de contador (mismo ID que usa el banco).
        /// </summary>
        [HttpGet("contador/{numeroContador}/comprobante-pago")]
        [ProducesResponseType(typeof(ComprobantePagoBancoDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ObtenerComprobantePago(string numeroContador)
        {
            if (string.IsNullOrWhiteSpace(numeroContador))
                return BadRequest(new { mensaje = "NumeroContador es requerido." });

            try
            {
                var comprobante = await _energiaService.ObtenerComprobantePagoPorContadorAsync(numeroContador);
                if (comprobante is null)
                    return NotFound(new { mensaje = $"No se encontró el contador '{numeroContador}'." });
                return Ok(comprobante);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { mensaje = ex.Message });
            }
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
        /// <summary>
        /// Lista paginada de clientes registrados. Acepta filtro de búsqueda
        /// parcial sobre DPI, nombre, apellido o correo (case-insensitive).
        /// El tamaño de página máximo se acota internamente para evitar que
        /// un cliente del API solicite cargas demasiado pesadas.
        /// </summary>
        /// <param name="pagina">Número de página (1-based). Default: 1.</param>
        /// <param name="tamanoPagina">Filas por página. Default: 50, máximo 200.</param>
        /// <param name="busqueda">Texto opcional para filtrar.</param>
        [HttpGet("clientes")]
        [ProducesResponseType(typeof(PaginadoDto<ClienteResumenDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ObtenerTodosLosClientes(
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 50,
            [FromQuery] string? busqueda = null)
        {
            var resultado = await _clientesService.ObtenerTodosLosClientesAsync(
                pagina, tamanoPagina, busqueda);
            return Ok(resultado);
        }

        /// <summary>
        /// Totales de recaudación para el panel de agencia. El campo
        /// <c>totalRecaudado</c> aplica la misma regla 95/5 que usa el banco
        /// y debe coincidir con el saldo de la cuenta prestadora "Energía
        /// Eléctrica" en el API Banco.
        /// </summary>
        [HttpGet("totales")]
        [ProducesResponseType(typeof(TotalesRecaudacionDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> ObtenerTotalesRecaudacion()
        {
            var totales = await _energiaService.ObtenerTotalesRecaudacionAsync();
            return Ok(totales);
        }
    }
    
}
