using System.ComponentModel.DataAnnotations;

namespace ApiEnergia.DTOs
{
    public record LoginRequest(
        [property: Required] string Credencial,
        [property: Required] string Password);

    public record LoginResponse(string Token, string Rol, string NombreUsuario);

    public record CambiarPasswordRequest(
        [property: Required] string PasswordActual,
        [property: Required, MinLength(6)] string PasswordNueva);
}
