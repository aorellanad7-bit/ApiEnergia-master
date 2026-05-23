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
}
