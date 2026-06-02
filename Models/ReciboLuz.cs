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

    /// <summary>
    /// Los nombres deben coincidir EXACTAMENTE con los valores del ENUM
    /// `recibo_luz.estado` en MySQL: ('Pendiente', 'Pagado', 'Vencido').
    /// EF Core los persiste con HasConversion&lt;string&gt;() (ver
    /// <c>EnergiaDbContext.OnModelCreating</c>); si la BD trae un valor que
    /// no esté en este enum, EF lanza una excepción al materializar la fila.
    /// </summary>
    public enum ReciboEstado
    {
        Pendiente,
        Pagado,
        Vencido
    }
}
