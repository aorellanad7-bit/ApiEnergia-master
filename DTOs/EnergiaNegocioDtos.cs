using System.ComponentModel.DataAnnotations;

namespace ApiEnergia.DTOs
{
    public record ConsultarDeudaResponseDto(string NumeroContador, decimal SaldoPendiente);

    public record NotificacionPagoBancoDto(
        [property: Required, MaxLength(30)] string NumeroContador,
        [property: Range(0.01, double.MaxValue)] decimal Monto);

    public record PagoEfectivoAgenciaDto(
        [property: Required, MaxLength(30)] string NumeroContador,
        [property: Range(0.01, double.MaxValue)] decimal MontoRecibido);

    public record RegistrarLecturaDto(
        [property: Required, MaxLength(30)] string NumeroContador,
        [property: Range(1, int.MaxValue)] int Kilovatios);
}
