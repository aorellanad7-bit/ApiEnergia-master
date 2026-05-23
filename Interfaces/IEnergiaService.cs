using ApiEnergia.DTOs;
using ApiEnergia.Models;

namespace ApiEnergia.Interfaces
{
    public interface IEnergiaService
    {
        Task<ReciboLuz> RegistrarLecturaAsync(string numeroContador, int kilovatios);

        Task<decimal> ConsultarDeudaTotalAsync(string numeroContador);

        /// <summary>
        /// Procesa un pago notificado por el Banco. Valida monto contra saldo total,
        /// registra el pago en pagos_procesados con canal "BANCO_*" y guarda la
        /// referencia bancaria en codigo_autorizacion_banco.
        /// </summary>
        Task<ResultadoPagoDto> ProcesarPagoExternoAsync(
            string numeroContador,
            decimal monto,
            string? referenciaBanco = null);

        /// <summary>
        /// Procesa un pago en efectivo recibido en agencia local.
        /// Permite pagos parciales (FIFO).
        /// </summary>
        Task<ResultadoPagoDto> ProcesarPagoEfectivoAsync(string numeroContador, decimal monto);

        // ── Consultas del portal de cliente ──────────────────────────────

        /// <summary>
        /// Resumen completo del cliente identificado por DPI: datos personales,
        /// listado de contadores y saldo pendiente por contador y total.
        /// </summary>
        Task<MiCuentaResponseDto?> ObtenerCuentaPorDpiAsync(string dpi);

        /// <summary>
        /// Lista los recibos del cliente identificado por DPI. Acepta filtros
        /// opcionales por contador y estado. Ordenados de más reciente a más antiguo.
        /// </summary>
        Task<IReadOnlyList<ReciboResumenDto>> ListarRecibosPorDpiAsync(
            string dpi,
            string? numeroContador = null,
            string? estado = null);

        /// <summary>
        /// Lista los pagos asociados a un recibo. Verifica que el recibo
        /// pertenezca a un contador del cliente con el DPI dado; si no,
        /// devuelve null para que el controlador devuelva 404 sin filtrar
        /// información sensible.
        /// </summary>
        Task<IReadOnlyList<PagoResumenDto>?> ListarPagosDeReciboAsync(string dpi, int idRecibo);
    }
}
