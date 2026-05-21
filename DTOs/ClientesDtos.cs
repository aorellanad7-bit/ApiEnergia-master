using System.ComponentModel.DataAnnotations;

namespace ApiEnergia.DTOs
{
    public record CrearClienteConContadorRequest(
        [property: Required, MaxLength(20)] string Dpi,
        [property: Required, MaxLength(100)] string Nombre,
        [property: Required, MaxLength(100)] string Apellido,
        [property: Required, MaxLength(150), EmailAddress] string Correo,
        [property: Required, MaxLength(255)] string DireccionInmueble);

    public record CrearClienteConContadorResponse(string NumeroContador, string UsuarioAsignado, string PasswordTemporal);
}
