using TrafficControl.Tasas.Api.Utils;

namespace TrafficControl.Tasas.Api.Services
{
    /// <summary>Escribe las métricas acumuladas cada IntervaloMetricasSeg segundos.</summary>
    public class MetricasWorker : BackgroundService
    {
        private readonly TimeSpan _intervalo;

        public MetricasWorker(ObservabilidadOptions opciones)
        {
            _intervalo = TimeSpan.FromSeconds(Math.Max(10, opciones.IntervaloMetricasSeg));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            clsMetricas.Iniciar();
            clsLog.GenerandoLog("MetricasWorker",
                $"Métricas iniciadas. Intervalo: {_intervalo.TotalSeconds} s", NivelLog.Info);

            using var timer = new PeriodicTimer(_intervalo);
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                    clsMetricas.Volcar();
            }
            catch (OperationCanceledException)
            {
                // Detención normal de la API
            }

            clsMetricas.Volcar();   // último periodo antes de detenerse
        }
    }
}