using ApiEnergia.DTOs;
using ApiEnergia.Interfaces;
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

        public AuthController(IUnitOfWork unitOfWork, IConfiguration configuration)
        {
            _unitOfWork = unitOfWork;
            _configuration = configuration;
        }

        [HttpPost("login")]
        [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var usuario = (await _unitOfWork.Accesos.FindAsync(u => u.NombreUsuario == request.Credencial))
                .FirstOrDefault();
            if (usuario is null || usuario.PasswordHash != request.Password)
                return Unauthorized(new { mensaje = "Credenciales inválidas." });

            var token = GenerarToken(usuario.NombreUsuario, usuario.Rol);
            return Ok(new LoginResponse(token, usuario.Rol));
        }

        private string GenerarToken(string usuario, string rol)
        {
            var secret = _configuration["Jwt:Key"] ?? "DEV_SECRET_KEY";
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
