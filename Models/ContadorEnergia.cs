namespace ApiEnergia.Models
{
    public class ContadorEnergia
    {
        public string NumeroContador { get; set; } = string.Empty;

        public int IdCliente { get; set; }

        public string DireccionInmueble { get; set; } = string.Empty;

        public DateTime FechaInstalacion { get; set; }

        public string Estado { get; set; } = "ACTIVO";

        public ClienteLuz? Cliente { get; set; }
    }
}
