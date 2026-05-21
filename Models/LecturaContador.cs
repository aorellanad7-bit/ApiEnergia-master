namespace ApiEnergia.Models
{
    public class LecturaContador
    {
        public int IdLectura { get; set; }

        public string NumeroContador { get; set; } = null!;

        public int KilovatiosConsumidos { get; set; }

        public DateTime FechaLectura { get; set; }
    }
}
