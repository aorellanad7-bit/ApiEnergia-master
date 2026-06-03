using System.ComponentModel.DataAnnotations;

namespace ApiEnergia.DTOs
{
    public record ConsultarDeudaResponseDto(string NumeroContador, decimal SaldoPendiente);

    /// <summary>
    /// Datos para imprimir un comprobante de pago en ventanilla bancaria.
    /// El número de comprobante es el mismo identificador que usa el banco: el contador.
    /// </summary>
    public record ComprobanteDetalleLineaDto(string Descripcion, decimal Monto);

    public record ComprobantePagoBancoDto(
        string NumeroContador,
        string NombreCliente,
        string ApellidoCliente,
        string Dpi,
        string DireccionInmueble,
        decimal MontoAPagar,
        DateTime FechaEmision,
        IReadOnlyList<ComprobanteDetalleLineaDto> Detalle);

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
        [Range(1, int.MaxValue)] int Kilovatios,
        [Range(2000, 2100)] int Anio,
        [Range(1, 12)] int Mes);

    public record RegistrarLecturaResponseDto(
        string NumeroContador,
        int Anio,
        int Mes,
        string PeriodoEtiqueta,
        int Kilovatios,
        decimal MontoGenerado,
        decimal SaldoPendiente,
        int IdRecibo);

    public record LecturaPeriodoDisponibleDto(
        bool Disponible,
        string PeriodoEtiqueta,
        string? Mensaje);

    /// <summary>
    /// Resultado consolidado del procesamiento de un pago externo (Banco) o interno (Agencia).
    /// </summary>
    /// <param name="CambioADevolver">
    /// Solo aplica al canal "OFICINA_EMPRESA": efectivo recibido por encima
    /// del saldo total que el cajero debe devolver al cliente. En pagos
    /// bancarios es siempre 0 porque el banco cobra el saldo exacto.
    /// </param>
    /// <param name="YaProcesado">
    /// true cuando el pago se rechazó porque ya fue aplicado previamente con
    /// la misma referencia bancaria (idempotencia). Permite al banco distinguir
    /// "callback duplicado" de un error real y no marcar el débito como fallido.
    /// </param>
    public record ResultadoPagoDto(
        bool Exito,
        string? Mensaje,
        decimal MontoAplicado,
        decimal SaldoRestante,
        int RecibosAfectados,
        decimal CambioADevolver = 0m,
        bool YaProcesado = false);

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
        IReadOnlyList<ContadorResumenDto> Contadores,
        bool RequiereCambioPassword = false);

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

    /// <summary>
    /// Resumen de recaudación que ve el panel de agencia. Útil para conciliar
    /// con la cuenta interna del banco que recibe el 95% de cada pago
    /// (en producción: cuenta id=103 del API Banco).
    /// </summary>
    /// <param name="TotalRecaudado">
    /// Monto neto acreditado a la empresa eléctrica por canal bancario,
    /// aplicando la regla 95/5 que usa el banco. Debe coincidir con el saldo
    /// de la cuenta prestadora "Energía Eléctrica" en el API Banco.
    /// </param>
    /// <param name="TotalCobradoBruto">Suma de todos los pagos aplicados (banco + agencia).</param>
    /// <param name="TotalCobradoBanco">Suma bruta (100%) de los pagos cobrados por el banco.</param>
    /// <param name="TotalCobradoAgencia">Suma de pagos en efectivo recibidos en agencia.</param>
    /// <param name="ComisionesBanco">5% retenido por el banco sobre los pagos bancarios.</param>
    /// <param name="CantidadPagos">Cantidad total de filas en pagos_procesados.</param>
    public record TotalesRecaudacionDto(
        decimal TotalRecaudado,
        decimal TotalCobradoBruto,
        decimal TotalCobradoBanco,
        decimal TotalCobradoAgencia,
        decimal ComisionesBanco,
        int CantidadPagos);
}
