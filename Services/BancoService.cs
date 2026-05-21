namespace ApiEnergia.Services
{
    /// <summary>
    /// Simula la validación contra el servicio externo bancario.
    /// Reemplaza la lógica interna por la integración real cuando esté disponible.
    /// </summary>
    public class BancoService
    {
        private static readonly Random _rng = new();

        /// <summary>
        /// Valida el pago y retorna un código de autorización único.
        /// </summary>
        /// <param name="monto">Monto a autorizar.</param>
        /// <param name="numeroContador">Identificador del contador.</param>
        /// <returns>Código de autorización si es válido; null si fue rechazado.</returns>
        public Task<string?> AutorizarPagoAsync(decimal monto, string numeroContador)
        {
            if (monto <= 0 || string.IsNullOrWhiteSpace(numeroContador))
                return Task.FromResult<string?>(null);

            // Genera un código de autorización de 10 caracteres alfanuméricos en mayúsculas
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var codigo = new string(Enumerable.Range(0, 10)
                         .Select(_ => chars[_rng.Next(chars.Length)])
                         .ToArray());

            return Task.FromResult<string?>(codigo);
        }

        /// <summary>
        /// Verifica si un código de autorización tiene el formato correcto.
        /// </summary>
        public bool EsCodigoValido(string? codigo) =>
            !string.IsNullOrWhiteSpace(codigo) && codigo.Length is >= 6 and <= 50;
    }
}