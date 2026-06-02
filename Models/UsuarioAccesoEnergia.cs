namespace ApiEnergia.Models
{
    /// <summary>
    /// Credenciales y rol de un usuario que puede iniciar sesión en la API.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="IdCliente"/> es <b>nullable</b> de forma intencional: los
    /// usuarios con rol <c>CLIENTE</c> apuntan al <see cref="ClienteLuz"/>
    /// titular de la cuenta, mientras que los administradores
    /// (<c>ADMIN_AGENCIA</c>) no representan a un cuentahabiente y por tanto
    /// no tienen cliente asociado (<see cref="IdCliente"/> = <c>null</c>).
    /// </para>
    /// <para>
    /// Esto evita tener que insertar clientes "ficticios" en
    /// <c>cliente_luz</c> para satisfacer una FK obligatoria, lo cual ensucia
    /// el listado de clientes del panel de agencia.
    /// </para>
    /// </remarks>
    public class UsuarioAccesoEnergia
    {
        public int IdUsuario { get; set; }
        public int? IdCliente { get; set; }
        public string NombreUsuario { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Rol { get; set; } = string.Empty;
    }
}
