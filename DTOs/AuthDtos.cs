using System.ComponentModel.DataAnnotations;

namespace ApiEnergia.DTOs
{
    public record LoginRequest(
        [Required] string Credencial,
        [Required] string Password);

    public record LoginResponse(string Token, string Rol, string NombreUsuario);

    public record CambiarPasswordRequest(
        [Required] string PasswordActual,
        [Required, MinLength(6)] string PasswordNueva);
}
