using ApiEnergia.DTOs;
using ApiEnergia.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace ApiEnergia.Controllers
{
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
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> RegistrarLectura([FromBody] RegistrarLecturaDto dto)
        {
            var recibo = await _energiaService.RegistrarLecturaAsync(dto.NumeroContador, dto.Kilovatios);
            return Ok(new ConsultarDeudaResponseDto(recibo.NumeroContador, recibo.SaldoPendiente));
        }

        [HttpPost("cliente")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        public async Task<IActionResult> CrearCliente([FromBody] CrearClienteConContadorRequest request)
        {
            var respuesta = await _clientesService.CrearClienteConContadorAsync(request);
            return Created(string.Empty, respuesta);
        }

        [HttpPost("pago-efectivo")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> PagoEfectivo([FromBody] PagoEfectivoAgenciaDto dto)
        {
            await _energiaService.ProcesarPagoEfectivoAsync(dto.NumeroContador, dto.MontoRecibido);
            return Ok();
        }
    }
}
