using System.ComponentModel.DataAnnotations;

namespace ApiEnergia.DTOs
{
    public record LoginRequest(
        [property: Required] string Credencial,
        [property: Required] string Password);

    public record LoginResponse(string Token, string Rol);
}
