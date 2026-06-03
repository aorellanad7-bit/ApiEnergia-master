using ApiEnergia.DTOs;

namespace ApiEnergia.Interfaces
{
    public interface IClientesService
    {
        Task<CrearClienteConContadorResponse> CrearClienteConContadorAsync(CrearClienteConContadorRequest request);

        /// <summary>
        /// Devuelve clientes paginados como DTOs (sin entidades EF). El filtro
        /// <paramref name="busqueda"/> hace match parcial sobre DPI, nombre,
        /// apellido o correo (case-insensitive). Los parámetros de paginación
        /// se normalizan internamente a un rango seguro para evitar consultas
        /// pesadas (TamanoPagina máximo controlado).
        /// </summary>
        Task<PaginadoDto<ClienteResumenDto>> ObtenerTodosLosClientesAsync(
            int pagina = 1,
            int tamanoPagina = 50,
            string? busqueda = null);

        /// <summary>
        /// Genera una nueva contraseña temporal para el portal del cliente (DPI = usuario).
        /// </summary>
        Task<ResetPasswordClienteResponse> ResetearPasswordClienteAsync(string dpi);
    }
}
