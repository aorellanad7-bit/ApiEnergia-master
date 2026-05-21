namespace ApiEnergia.Models
{
    public class PagosProcesados
    {
        public int IdPago { get; set; }

        public int IdRecibo { get; set; }

        public string NumeroContador { get; set; } = null!;

        public decimal Monto { get; set; }

        public DateTime FechaCobro { get; set; }

        public string CanalPago { get; set; } = null!;

        public string? CodigoAutorizacionBanco { get; set; }

        public ReciboLuz Recibo { get; set; } = null!;
    }
}
