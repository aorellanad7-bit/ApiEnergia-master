using ApiEnergia.DTOs;
using ApiEnergia.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
        private readonly IEnergiaService _energiaService;

        public ClienteController(IEnergiaService energiaService)
        {
            _energiaService = energiaService;
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
    }
}
