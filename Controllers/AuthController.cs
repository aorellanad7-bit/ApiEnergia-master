using ApiEnergia.Auth;
using ApiEnergia.DTOs;
using ApiEnergia.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ApiEnergia.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class AuthController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IConfiguration _configuration;
        private readonly IPasswordHasher _passwordHasher;

        public AuthController(
            IUnitOfWork unitOfWork,
            IConfiguration configuration,
            IPasswordHasher passwordHasher)
        {
            _unitOfWork = unitOfWork;
            _configuration = configuration;
            _passwordHasher = passwordHasher;
        }

        [HttpPost("login")]
        [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var usuario = (await _unitOfWork.Accesos.FindAsync(u => u.NombreUsuario == request.Credencial))
                .FirstOrDefault();
            if (usuario is null)
                return Unauthorized(new { mensaje = "Credenciales inválidas." });

            // Camino normal: hash BCrypt → verificar
            if (_passwordHasher.EsHashBCrypt(usuario.PasswordHash))
            {
                if (!_passwordHasher.Verificar(request.Password, usuario.PasswordHash))
                    return Unauthorized(new { mensaje = "Credenciales inválidas." });
            }
            else
            {
                // Camino legacy: el password está guardado en plaintext (datos previos
                // a la migración). Si coincide, lo re-hasheamos en el momento para
                // que el próximo login ya use BCrypt. Compatibilidad hacia atrás.
                if (usuario.PasswordHash != request.Password)
                    return Unauthorized(new { mensaje = "Credenciales inválidas." });

                usuario.PasswordHash = _passwordHasher.Hash(request.Password);
                await _unitOfWork.SaveChangesAsync();
            }

            var token = GenerarToken(usuario.NombreUsuario, usuario.Rol);
            return Ok(new LoginResponse(token, usuario.Rol, usuario.NombreUsuario));
        }

        [HttpPost("cambiar-password")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> CambiarPassword([FromBody] CambiarPasswordRequest request)
        {
            var nombreUsuario = User.Identity?.Name;
            if (string.IsNullOrEmpty(nombreUsuario))
                return Unauthorized(new { mensaje = "Token inválido." });

            var usuario = (await _unitOfWork.Accesos.FindAsync(u => u.NombreUsuario == nombreUsuario))
                .FirstOrDefault();
            if (usuario is null)
                return Unauthorized(new { mensaje = "Usuario no encontrado." });

            // Verificar password actual (acepta tanto BCrypt como plaintext legacy)
            bool actualOk = _passwordHasher.EsHashBCrypt(usuario.PasswordHash)
                ? _passwordHasher.Verificar(request.PasswordActual, usuario.PasswordHash)
                : usuario.PasswordHash == request.PasswordActual;

            if (!actualOk)
                return BadRequest(new { mensaje = "La contraseña actual no coincide." });

            usuario.PasswordHash = _passwordHasher.Hash(request.PasswordNueva);
            await _unitOfWork.SaveChangesAsync();

            return NoContent();
        }

        private string GenerarToken(string usuario, string rol)
        {
            // Jwt:Key se valida en Program.cs al arrancar (presencia + longitud
            // mínima). Si llegamos aquí sin valor es un error de configuración
            // grave: preferimos fallar antes que firmar con un default inseguro.
            var secret = _configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("Jwt:Key no está configurado.");
            var issuer = _configuration["Jwt:Issuer"] ?? "ApiEnergia";
            var audience = _configuration["Jwt:Audience"] ?? "ApiEnergia";

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, usuario),
                new Claim(ClaimTypes.Role, rol)
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer,
                audience,
                claims,
                expires: DateTime.UtcNow.AddHours(2),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
