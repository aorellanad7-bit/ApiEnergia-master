using System.ComponentModel.DataAnnotations;

namespace ApiEnergia.DTOs
{
    public record CrearClienteConContadorRequest(
        [Required, MaxLength(20)] string Dpi,
        [Required, MaxLength(100)] string Nombre,
        [Required, MaxLength(100)] string Apellido,
        [Required, MaxLength(150), EmailAddress] string Correo,
        [Required, MaxLength(255)] string DireccionInmueble);

    public record CrearClienteConContadorResponse(string NumeroContador, string UsuarioAsignado, string PasswordTemporal);

    /// <summary>
    /// Vista resumida de un cliente para listados. NO incluye colecciones
    /// navegables (Contadores) para evitar serializar grafos completos y
    /// reducir la carga útil enviada al frontend.
    /// </summary>
    public record ClienteResumenDto(
        int IdCliente,
        string Dpi,
        string Nombre,
        string Apellido,
        string Correo,
        int CantidadContadores);

    /// <summary>
    /// Sobre genérico de paginación. Devuelve la página solicitada junto con
    /// la metadata necesaria para que el frontend pueda renderizar el
    /// paginador (página actual, total de registros y total de páginas).
    /// </summary>
    public record PaginadoDto<T>(
        int Pagina,
        int TamanoPagina,
        int TotalRegistros,
        int TotalPaginas,
        IReadOnlyList<T> Items);
}
