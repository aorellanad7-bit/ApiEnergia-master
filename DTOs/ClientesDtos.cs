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
    /// Resumen breve de un contador asociado a un cliente, pensado para
    /// embeber dentro del listado paginado. Incluye lo que el panel de
    /// agencia necesita ver de un vistazo (dirección, estado, saldo
    /// pendiente) sin tener que pedir el detalle por cada cliente.
    /// </summary>
    public record ContadorBreveDto(
        string NumeroContador,
        string DireccionInmueble,
        string Estado,
        decimal SaldoPendiente);

    /// <summary>
    /// Vista resumida de un cliente para listados, con la lista de sus
    /// contadores asociados (datos relevantes para agencia: número,
    /// dirección, estado y saldo). NO incluye otras colecciones EF
    /// para evitar serializar grafos completos.
    /// </summary>
    public record ClienteResumenDto(
        int IdCliente,
        string Dpi,
        string Nombre,
        string Apellido,
        string Correo,
        int CantidadContadores,
        decimal SaldoTotalPendiente,
        IReadOnlyList<ContadorBreveDto> Contadores);

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
