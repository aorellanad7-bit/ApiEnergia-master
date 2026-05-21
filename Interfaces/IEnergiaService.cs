using ApiEnergia.Models;

namespace ApiEnergia.Interfaces
{
    public interface IEnergiaService
    {
        Task<ReciboLuz> RegistrarLecturaAsync(string numeroContador, int kilovatios);
        Task<decimal> ConsultarDeudaTotalAsync(string numeroContador);
        Task ProcesarPagoExternoAsync(string numeroContador, decimal monto);
        Task ProcesarPagoEfectivoAsync(string numeroContador, decimal monto);
    }
}
