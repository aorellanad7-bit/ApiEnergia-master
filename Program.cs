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

            // 1. Configurar CORS (Obligatorio para que Next.js o el Banco no sean bloqueados)
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("NextJsPolicy", policy =>
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

            // 5. Inyección de Servicios Externos (Integración HTTP para llamar al Banco)
            builder.Services.AddHttpClient();

            // 6. Autenticación JWT (Específico de Energía para el Portal de Clientes)
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

            app.UseHttpsRedirection();

            // 7. Activar CORS (Debe ir antes del Authentication/Authorization)
            app.UseCors("NextJsPolicy");

            // 8. Pipeline de Seguridad
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}