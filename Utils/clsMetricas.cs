using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace TrafficControl.Tasas.Api.Utils
{
    /// <summary>
    /// Métricas de la API: acumula en memoria las peticiones por endpoint y,
    /// en cada intervalo, escribe una línea en METRICAS_dd_MM_yyyy.txt con
    /// peticiones, tiempos de respuesta, códigos HTTP, CPU, memoria e hilos.
    /// </summary>
    public static class clsMetricas
    {
        private sealed class Acumulado
        {
            public long Peticiones;
            public long TotalMs;
            public long MaxMs;
            public readonly Dictionary<int, long> Codigos = new();
        }

        private static readonly object _bloqueo = new();
        private static Dictionary<string, Acumulado> _actual = new();
        private static DateTime _inicioPeriodo = DateTime.Now;
        private static TimeSpan _cpuAnterior = TimeSpan.Zero;

        /// <summary>Se llama una vez al arrancar el servicio de métricas.</summary>
        public static void Iniciar()
        {
            using var proceso = Process.GetCurrentProcess();
            lock (_bloqueo)
            {
                _inicioPeriodo = DateTime.Now;
                _cpuAnterior = proceso.TotalProcessorTime;
            }
        }

        /// <summary>Registra una petición atendida (lo llama MetricasMiddleware).</summary>
        public static void Registrar(string endpoint, int codigoHttp, long milisegundos)
        {
            lock (_bloqueo)
            {
                if (!_actual.TryGetValue(endpoint, out var a))
                    _actual[endpoint] = a = new Acumulado();

                a.Peticiones++;
                a.TotalMs += milisegundos;
                if (milisegundos > a.MaxMs)
                    a.MaxMs = milisegundos;
                a.Codigos[codigoHttp] = a.Codigos.GetValueOrDefault(codigoHttp) + 1;
            }
        }

        /// <summary>Escribe la línea del periodo y reinicia los contadores.</summary>
        public static void Volcar()
        {
            try
            {
                Dictionary<string, Acumulado> datos;
                DateTime inicio;

                lock (_bloqueo)
                {
                    datos = _actual;
                    _actual = new Dictionary<string, Acumulado>();
                    inicio = _inicioPeriodo;
                    _inicioPeriodo = DateTime.Now;
                }

                var segundos = Math.Max(1, (DateTime.Now - inicio).TotalSeconds);

                using var proceso = Process.GetCurrentProcess();
                var cpu = proceso.TotalProcessorTime;
                var cpuPct = (cpu - _cpuAnterior).TotalMilliseconds / (segundos * 1000 * Environment.ProcessorCount) * 100;
                _cpuAnterior = cpu;

                var total = datos.Values.Sum(d => d.Peticiones);
                var inv = CultureInfo.InvariantCulture;

                var sb = new StringBuilder();
                sb.Append(string.Create(inv,
                    $"| Periodo {segundos:0} s | Peticiones {total} ({total / segundos:0.0}/s) | CPU {cpuPct:0.0} % | " +
                    $"Memoria {proceso.WorkingSet64 / 1048576} MB | GC {GC.GetTotalMemory(false) / 1048576} MB | Hilos {proceso.Threads.Count}"));

                foreach (var (endpoint, d) in datos.OrderBy(x => x.Key))
                {
                    var codigos = string.Join(" ", d.Codigos.OrderBy(c => c.Key).Select(c => $"{c.Key}:{c.Value}"));
                    sb.Append(string.Create(inv,
                        $" | {endpoint}: {d.Peticiones} req, prom {d.TotalMs / d.Peticiones} ms, máx {d.MaxMs} ms [{codigos}]"));
                }

                clsLog.EscribirMetrica(sb.ToString());
            }
            catch
            {
                // Las métricas nunca deben interrumpir la API
            }
        }
    }
}