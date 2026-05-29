using System.ComponentModel.DataAnnotations;

namespace ApiEnergia.Services
{
    /// <summary>
    /// Tarifa aplicada al registrar una lectura del contador. Se enlaza desde
    /// la sección <c>Tarifa</c> de <c>appsettings</c>. Antes la tarifa estaba
    /// hardcoded en <see cref="EnergiaService.RegistrarLecturaAsync"/> como
    /// <c>1.50m</c>; con esto se puede cambiar sin recompilar.
    /// </summary>
    public sealed class TarifaOptions
    {
        public const string SectionName = "Tarifa";

        /// <summary>
        /// Precio en quetzales por cada kWh consumido. Debe ser estrictamente
        /// positivo; el binder rechaza valores ≤ 0 al arrancar.
        /// </summary>
        [Range(0.0001, double.MaxValue, ErrorMessage = "PrecioPorKwh debe ser mayor a 0.")]
        public decimal PrecioPorKwh { get; set; } = 1.50m;
    }
}
