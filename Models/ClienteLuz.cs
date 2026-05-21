namespace ApiEnergia.Models
{
    public class ClienteLuz
    {
        public int IdCliente { get; set; }
        public string Dpi { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Apellido { get; set; } = string.Empty;
        public string Correo { get; set; } = string.Empty;

        public ICollection<ContadorEnergia> Contadores { get; set; } = new List<ContadorEnergia>();
    }
}
