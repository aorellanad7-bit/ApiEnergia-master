namespace ApiEnergia.Models
{
    public class LecturaContador
    {
        public int IdLectura { get; set; }

        public string NumeroContador { get; set; } = null!;

        public int KilovatiosConsumidos { get; set; }

        public DateTime FechaLectura { get; set; }

        /// <summary>Periodo facturado (año calendario). Evita duplicados por mes.</summary>
        public int PeriodoAnio { get; set; }

        /// <summary>Periodo facturado (1-12). Evita duplicados por mes.</summary>
        public int PeriodoMes { get; set; }
    }
}
