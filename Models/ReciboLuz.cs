namespace ApiEnergia.Models
{
    public class ReciboLuz
    {
        public int IdRecibo { get; set; }

        public int LecturaContadorId { get; set; }

        public string NumeroContador { get; set; } = null!;

        public decimal MontoTotal { get; set; }

        public decimal SaldoPendiente { get; set; }

        public DateTime FechaEmision { get; set; }

        public ReciboEstado Estado { get; set; }

        public LecturaContador? LecturaContador { get; set; }

        public ICollection<PagosProcesados> Pagos { get; set; } = new List<PagosProcesados>();
    }

    public enum ReciboEstado
    {
        Pendiente,
        Pagado
    }
}
