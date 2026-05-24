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

    /// <summary>
    /// Datos que envía el cliente desde el portal para pagar el saldo total de un contador.
    /// El monto NO viene del cliente: la API lo calcula con el saldo pendiente actual y
    /// se lo entrega al banco para evitar manipulación.
    /// </summary>
    public record PagarSaldoClienteDto(
        [Required, MaxLength(30)] string NumeroContador,
        [Required, MaxLength(20)] string NumeroTarjeta,
        [Required, MaxLength(10)] string Pin,
        [MaxLength(100)] string? ReferenciaCliente = null);

    /// <summary>
    /// Respuesta del orquestador de pago del portal: incluye lo aplicado por Energía y
    /// la respuesta cruda del banco para que el frontend pueda mostrar trazabilidad.
    /// </summary>
    /// <param name="AplicadoEnEnergia">
    /// true cuando el callback del banco logró marcar los recibos como pagados en Energía.
    /// false significa que el banco cobró pero el saldo NO bajó: el frontend debe alertar.
    /// </param>
    /// <param name="ReferenciaBanco">
    /// idDebito reportado por el banco. Sirve para soporte/conciliación si AplicadoEnEnergia=false.
    /// </param>
    public record PagarSaldoClienteResponseDto(
        string NumeroContador,
        decimal MontoPagado,
        decimal SaldoRestante,
        bool AplicadoEnEnergia,
        string? ReferenciaBanco,
        string Mensaje,
        object? RespuestaBanco);

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
