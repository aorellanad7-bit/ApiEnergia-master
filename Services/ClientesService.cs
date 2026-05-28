using ApiEnergia.Auth;
using ApiEnergia.DTOs;
using ApiEnergia.Interfaces;
using ApiEnergia.Models;

namespace ApiEnergia.Services
{
    public class ClientesService : IClientesService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPasswordHasher _passwordHasher;

        public ClientesService(IUnitOfWork unitOfWork, IPasswordHasher passwordHasher)
        {
            _unitOfWork = unitOfWork;
            _passwordHasher = passwordHasher;
        }

        public async Task<CrearClienteConContadorResponse> CrearClienteConContadorAsync(CrearClienteConContadorRequest request)
        {
            if (request is null)
                throw new ArgumentNullException(nameof(request));

            var cliente = (await _unitOfWork.Clientes.FindAsync(c => c.Dpi == request.Dpi)).FirstOrDefault();
            if (cliente is null)
            {
                cliente = new ClienteLuz
                {
                    Dpi = request.Dpi,
                    Nombre = request.Nombre,
                    Apellido = request.Apellido,
                    Correo = request.Correo
                };

                await _unitOfWork.Clientes.AddAsync(cliente);
                await _unitOfWork.SaveChangesAsync();
            }

            var numeroContador = GenerarNumeroContador();
            var passwordTemporal = $"Temp{request.Dpi.Substring(0, 4)}!";
            var contador = new ContadorEnergia
            {
                NumeroContador = numeroContador,
                DireccionInmueble = request.DireccionInmueble,
                FechaInstalacion = DateTime.UtcNow,
                Estado = "ACTIVO",
                Cliente = cliente
            };

            await _unitOfWork.Accesos.AddAsync(new UsuarioAccesoEnergia
            {
                IdCliente = cliente.IdCliente,
                NombreUsuario = request.Dpi,
                PasswordHash = _passwordHasher.Hash(passwordTemporal),
                Rol = "CLIENTE"
            });

            await _unitOfWork.Contadores.AddAsync(contador);

            await _unitOfWork.SaveChangesAsync();

            return new CrearClienteConContadorResponse(numeroContador, request.Dpi, passwordTemporal);
        }
        public async Task<IReadOnlyList<ClienteLuz>> ObtenerTodosLosClientesAsync()
        {
            // Usamos la unidad de trabajo y el repositorio de clientes.
            // Pasamos una expresión lambda vacía (c => true) para que traiga TODOS sin filtrar.
            var clientes = await _unitOfWork.Clientes.FindAsync(c => true);
            
            // Lo convertimos a una lista de solo lectura para cumplir con la firma
            return clientes.ToList().AsReadOnly();
        }

        private static string GenerarNumeroContador()
        {
            return Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        }
    }
}
