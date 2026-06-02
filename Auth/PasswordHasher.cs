namespace ApiEnergia.Auth
{
    public interface IPasswordHasher
    {
        string Hash(string passwordPlano);
        bool Verificar(string passwordPlano, string hashAlmacenado);
        bool EsHashBCrypt(string valor);
    }

    /// <summary>
    /// Hasher de passwords basado en BCrypt. WorkFactor 11 = ~150ms por hash en
    /// hardware moderno (balance entre seguridad y latencia de login).
    /// </summary>
    public sealed class PasswordHasher : IPasswordHasher
    {
        private const int WorkFactor = 11;

        public string Hash(string passwordPlano)
        {
            if (string.IsNullOrWhiteSpace(passwordPlano))
                throw new ArgumentException("La contraseña no puede ser vacía.", nameof(passwordPlano));

            return BCrypt.Net.BCrypt.HashPassword(passwordPlano, WorkFactor);
        }

        public bool Verificar(string passwordPlano, string hashAlmacenado)
        {
            if (string.IsNullOrEmpty(passwordPlano) || string.IsNullOrEmpty(hashAlmacenado))
                return false;

            try
            {
                return BCrypt.Net.BCrypt.Verify(passwordPlano, hashAlmacenado);
            }
            catch (BCrypt.Net.SaltParseException)
            {
                return false;
            }
        }

        /// <summary>
        /// Detecta si una cadena es un hash BCrypt válido. Sirve para identificar
        /// passwords almacenadas en plaintext que necesitan migrarse.
        /// </summary>
        public bool EsHashBCrypt(string valor)
        {
            if (string.IsNullOrEmpty(valor) || valor.Length < 60) return false;
            return valor.StartsWith("$2a$", StringComparison.Ordinal)
                || valor.StartsWith("$2b$", StringComparison.Ordinal)
                || valor.StartsWith("$2x$", StringComparison.Ordinal)
                || valor.StartsWith("$2y$", StringComparison.Ordinal);
        }
    }
}
