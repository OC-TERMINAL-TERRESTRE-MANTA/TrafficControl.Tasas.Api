namespace TrafficControl.Tasas.Api.Utils
{
    /// <summary>Sección "Observabilidad" de appsettings.json.</summary>
    public class ObservabilidadOptions
    {
        /// <summary>Carpeta raíz de los logs y las métricas.</summary>
        public string RutaLog { get; set; } = @"C:\BIOMETRIC_LOG";

        /// <summary>Días que se conservan los .zip de días anteriores (0 = sin límite).</summary>
        public int DiasRetencion { get; set; } = 30;

        /// <summary>Cada cuántos segundos se escribe una línea de métricas (mínimo 10).</summary>
        public int IntervaloMetricasSeg { get; set; } = 60;
    }
}