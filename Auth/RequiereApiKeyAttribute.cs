using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ApiEnergia.Auth
{
    /// <summary>
    /// Filtro de autorización que valida un API key compartido entre el Banco y
    /// Energía. El cliente debe enviar el header <c>X-Api-Key</c> con el valor
    /// configurado en <c>Banco:ApiKey</c>. La comparación se hace en tiempo
    /// constante para mitigar ataques de timing.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RequiereApiKeyAttribute : Attribute, IAuthorizationFilter
    {
        public const string HeaderName = "X-Api-Key";
        public const string ConfigKey = "Banco:ApiKey";

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var apiKeyRecibida)
                || string.IsNullOrWhiteSpace(apiKeyRecibida))
            {
                context.Result = new UnauthorizedObjectResult(new
                {
                    mensaje = $"Falta header {HeaderName}."
                });
                return;
            }

            var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var apiKeyEsperada = configuration[ConfigKey];

            if (string.IsNullOrWhiteSpace(apiKeyEsperada))
            {
                // No bloqueamos por config faltante en server, pero sí lo hacemos visible
                context.Result = new ObjectResult(new
                {
                    mensaje = $"El servidor no tiene configurado {ConfigKey}."
                })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
                return;
            }

            if (!CompararEnTiempoConstante(apiKeyRecibida.ToString(), apiKeyEsperada))
            {
                context.Result = new UnauthorizedObjectResult(new
                {
                    mensaje = "API key inválida."
                });
            }
        }

        private static bool CompararEnTiempoConstante(string a, string b)
        {
            var bytesA = Encoding.UTF8.GetBytes(a);
            var bytesB = Encoding.UTF8.GetBytes(b);
            return bytesA.Length == bytesB.Length
                && CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
        }
    }
}
