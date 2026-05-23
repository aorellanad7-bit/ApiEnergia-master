using System.ComponentModel.DataAnnotations;

namespace ApiEnergia.DTOs
{
    public record ConsultarDeudaResponseDto(string NumeroContador, decimal SaldoPendiente);

    /// <summary>
    /// Notificación de pago que el Banco envía al endpoint público de Energía.
    /// </summary>
    public record NotificacionPagoBancoDto(
        [Required, MaxLength(30)] string NumeroContador,
        [Range(0.01, double.MaxValue)] decimal Monto,
        [MaxLength(50)] string? ReferenciaBanco = null);

    public record PagoEfectivoAgenciaDto(
        [Required, MaxLength(30)] string NumeroContador,
        [Range(0.01, double.MaxValue)] decimal MontoRecibido);

    public record RegistrarLecturaDto(
        [Required, MaxLength(30)] string NumeroContador,
        [Range(1, int.MaxValue)] int Kilovatios);

    /// <summary>
    /// Resultado consolidado del procesamiento de un pago externo (Banco) o interno (Agencia).
    /// </summary>
    public record ResultadoPagoDto(
        bool Exito,
        string? Mensaje,
        decimal MontoAplicado,
        decimal SaldoRestante,
        int RecibosAfectados);

    // ── Consultas del portal de cliente ──────────────────────────────────────

    public record ContadorResumenDto(
        string NumeroContador,
        string DireccionInmueble,
        string Estado,
        DateTime FechaInstalacion,
        decimal SaldoPendiente);

    public record MiCuentaResponseDto(
        int IdCliente,
        string Dpi,
        string Nombre,
        string Apellido,
        string Correo,
        decimal SaldoTotalPendiente,
        IReadOnlyList<ContadorResumenDto> Contadores);

    public record ReciboResumenDto(
        int IdRecibo,
        string NumeroContador,
        DateTime FechaEmision,
        decimal MontoTotal,
        decimal SaldoPendiente,
        string Estado);

    public record PagoResumenDto(
        int IdPago,
        int IdRecibo,
        string NumeroContador,
        decimal Monto,
        DateTime FechaCobro,
        string CanalPago,
        string? CodigoAutorizacionBanco);
}
