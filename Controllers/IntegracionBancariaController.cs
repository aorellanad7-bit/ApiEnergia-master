using ApiEnergia.DTOs;
using ApiEnergia.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ApiEnergia.Controllers
{
    [ApiController]
    [Route("api/Energia/Banco")]
    [Produces("application/json")]
    [Tags("Integración Bancaria")]
    public class IntegracionBancariaController : ControllerBase
    {
        private readonly IEnergiaService _energiaService;

        public IntegracionBancariaController(IEnergiaService energiaService)
        {
            _energiaService = energiaService;
        }

        [HttpGet("consultar/{numeroContador}")]
        [ProducesResponseType(typeof(ConsultarDeudaResponseDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> ConsultarDeuda(string numeroContador)
        {
            var saldo = await _energiaService.ConsultarDeudaTotalAsync(numeroContador);
            return Ok(new ConsultarDeudaResponseDto(numeroContador, saldo));
        }

        [HttpPost("pagar")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Pagar([FromBody] NotificacionPagoBancoDto dto)
        {
            await _energiaService.ProcesarPagoExternoAsync(dto.NumeroContador, dto.Monto);
            return Ok();
        }
    }
}
