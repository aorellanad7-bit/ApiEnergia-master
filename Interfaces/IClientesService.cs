using ApiEnergia.DTOs;

namespace ApiEnergia.Interfaces
{
    public interface IClientesService
    {
        Task<CrearClienteConContadorResponse> CrearClienteConContadorAsync(CrearClienteConContadorRequest request);
    }
}
