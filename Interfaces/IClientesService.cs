using ApiEnergia.DTOs;
using ApiEnergia.Models;

namespace ApiEnergia.Interfaces
{
    public interface IClientesService
    {
        Task<CrearClienteConContadorResponse> CrearClienteConContadorAsync(CrearClienteConContadorRequest request);

        Task<IReadOnlyList<ClienteLuz>> ObtenerTodosLosClientesAsync();
    }
}
