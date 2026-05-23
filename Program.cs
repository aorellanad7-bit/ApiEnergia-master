using ApiEnergia.Auth;
using ApiEnergia.DbContext;
using ApiEnergia.Interfaces;
using ApiEnergia.Repositories;
using ApiEnergia.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using System.Text;

namespace ApiEnergia
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 1. Configurar CORS — política abierta para que el Banco, otros servicios
            //    y cualquier frontend (Next.js, HTML estático, Scalar, etc.) puedan
            //    consumir esta API sin restricciones de origen.
            const string corsPolicyName = "PermitirTodo";
            builder.Services.AddCors(options =>
            {
                options.AddPolicy(corsPolicyName, policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyHeader()
                          .AllowAnyMethod();
                });
            });

            // 2. Extraer la cadena de conexión
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
                

            // 3. Registrar el DbContext con Pomelo MySQL (Igual que en el Banco)
            builder.Services.AddDbContext<EnergiaDbContext>(options =>
                options.UseMySql(
                    connectionString,
                    new MySqlServerVersion(new Version(8, 0, 32)),
                    mySqlOptions =>
                    {
                        mySqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                        mySqlOptions.CommandTimeout(30);
                    }
                )
            );

            // Controladores con serialización JSON estándar
            builder.Services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                    options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                    options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
                });

            builder.Services.AddOpenApi();

            // 4. Inyección de la Capa de Aplicación e Infraestructura
            builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
            builder.Services.AddScoped<IEnergiaService, EnergiaService>();
            builder.Services.AddScoped<IClientesService, ClientesService>();
            builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();

            // HttpClient con el que el portal del cliente orquesta el cobro contra
            // el API Banco (endpoint POST api/Pagos/ejecutar). La URL del banco se
            // configura con BancoApi:Url en appsettings; si no está, fallamos rápido
            // en arranque para evitar errores opacos en producción.
            var bancoApiUrl = builder.Configuration["BancoApi:Url"];
            if (string.IsNullOrWhiteSpace(bancoApiUrl))
                throw new InvalidOperationException(
                    "Falta configurar BancoApi:Url en appsettings (URL base del API Banco).");

            builder.Services.AddHttpClient("BancoApi", client =>
            {
                client.BaseAddress = new Uri(bancoApiUrl);
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            // 5. Autenticación JWT (Específico de Energía para el Portal de Clientes)
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    var secret = builder.Configuration["Jwt:Key"] ?? "DEV_SECRET_KEY";
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "ApiEnergia",
                        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "ApiEnergia",
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))
                    };
                });

            builder.Services.AddAuthorization();

            // 6. Filtro de API key compartido (registrado por reflexión vía
            //    [RequiereApiKey] sobre IntegracionBancariaController). Lee
            //    Banco:ApiKey de IConfiguration en runtime.

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
            {
                app.MapScalarApiReference(options =>
                {
                    options.Title = "API Energía Eléctrica";
                    options.Theme = ScalarTheme.DeepSpace;
                });
                app.MapOpenApi();
            }

            // Middleware global de excepciones: garantiza que cualquier fallo no
            // controlado se traduzca a un JSON 500 con `tipo`, `mensaje` y
            // `innerMensaje`. Sin esto, IIS ASP.NET responde con cuerpo vacío y
            // es imposible diagnosticar errores en producción.
            app.Use(async (context, next) =>
            {
                try
                {
                    await next();
                }
                catch (Exception ex)
                {
                    var logger = context.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("UnhandledException");
                    logger.LogError(ex, "Excepción no controlada en {Path}", context.Request.Path);

                    if (!context.Response.HasStarted)
                    {
                        context.Response.Clear();
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        context.Response.ContentType = "application/json";

                        var payload = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            error = "Excepción no controlada en la API de Energía.",
                            tipo = ex.GetType().FullName,
                            mensaje = ex.Message,
                            innerMensaje = ex.InnerException?.Message,
                            path = context.Request.Path.Value
                        });
                        await context.Response.WriteAsync(payload);
                    }
                }
            });

            app.UseHttpsRedirection();

            // 7. Activar CORS — DEBE ir antes de Authentication/Authorization
            //    para que las peticiones preflight (OPTIONS) no se bloqueen.
            app.UseCors(corsPolicyName);

            // 8. Pipeline de seguridad: JWT primero (para los endpoints con
            //    [Authorize]), luego AuthorizationFilters (incluido [RequiereApiKey]).
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}