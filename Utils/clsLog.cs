using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;

namespace TrafficControl.Tasas.Api.Utils
{
    /// <summary>Niveles de log exigidos por la Gerencia de Desarrollo.</summary>
    public enum NivelLog
    {
        Info,
        Warn,
        Error,
        Fatal
    }

    /// <summary>
    /// Log de texto en {LogPath}\{ProjectName}\
    ///   LOG_EVENT_dd_MM_yyyy.txt  → eventos (INFO, WARN, ERROR, FATAL)
    ///   METRICAS_dd_MM_yyyy.txt   → métricas (las escribe clsMetricas)
    /// Los días anteriores se comprimen en LOG_dd_MM_yyyy.zip y se eliminan
    /// los .zip con más de DiasRetencion días.
    /// Seguro para varias peticiones simultáneas.
    /// </summary>
    public static class clsLog
    {
        public static string ProjectName { get; set; } = "TrafficControl.Tasas.Api";
        public static string LogPath { get; set; } = @"C:\BIOMETRIC_LOG";

        /// <summary>Días que se conservan los .zip. 0 o menos = no se elimina nada.</summary>
        public static int DiasRetencion { get; set; } = 30;

        private const string PrefijoEventos = "LOG_EVENT_";
        private const string PrefijoMetricas = "METRICAS_";
        private const string PrefijoZip = "LOG_";
        private const string FormatoFechaArchivo = "dd_MM_yyyy";

        // Varias peticiones escriben a la vez: solo un hilo toca los archivos
        private static readonly object _bloqueo = new();
        private static DateTime _ultimoMantenimiento = DateTime.MinValue;

        // ------------------------------------------------------------------
        // API pública
        // ------------------------------------------------------------------

        public static void GenerandoLog(string funcName, string texto, NivelLog nivel)
            => Escribir(PrefijoEventos, $"{Etiqueta(nivel)} -> {DateTime.Now:HH:mm:ss.fff} {funcName}: {texto}");

        /// <summary>Compatibilidad con las llamadas existentes: true = ERROR, false = INFO.</summary>
        public static void GenerandoLog(string funcName, string texto, bool isError)
            => GenerandoLog(funcName, texto, isError ? NivelLog.Error : NivelLog.Info);

        /// <summary>Registra una excepción con su detalle completo (por defecto ERROR).</summary>
        public static void GenerandoLog(string funcName, string texto, Exception ex, NivelLog nivel = NivelLog.Error)
            => GenerandoLog(funcName, $"{texto} | {ex}", nivel);

        /// <summary>Escribe una línea en el archivo diario de métricas.</summary>
        public static void EscribirMetrica(string texto)
            => Escribir(PrefijoMetricas, $"METRICA -> {DateTime.Now:HH:mm:ss} {texto}");

        // ------------------------------------------------------------------
        // Escritura
        // ------------------------------------------------------------------

        private static string Etiqueta(NivelLog nivel) => nivel switch
        {
            NivelLog.Info => "INFO",
            NivelLog.Warn => "WARN",
            NivelLog.Error => "ERROR",
            NivelLog.Fatal => "FATAL",
            _ => "INFO"
        };

        private static void Escribir(string prefijo, string linea)
        {
            try
            {
                var hoy = DateTime.Now.Date;
                var carpeta = Path.Combine(LogPath, ProjectName);
                var archivo = Path.Combine(carpeta, $"{prefijo}{hoy:dd_MM_yyyy}.txt");

                lock (_bloqueo)
                {
                    Directory.CreateDirectory(carpeta);   // no hace nada si ya existe
                    File.AppendAllText(archivo, linea + Environment.NewLine);

                    // Compresión y retención una vez al día, no en cada escritura
                    if (_ultimoMantenimiento != hoy)
                    {
                        MantenimientoDiario(carpeta, hoy);
                        _ultimoMantenimiento = hoy;
                    }
                }
            }
            catch
            {
                // El log nunca debe interrumpir la API
            }
        }

        // ------------------------------------------------------------------
        // Compresión y retención
        // ------------------------------------------------------------------

        private static void MantenimientoDiario(string carpeta, DateTime hoy)
        {
            // 1. Comprimir los .txt de días anteriores (eventos y métricas) en LOG_dd_MM_yyyy.zip
            foreach (var prefijo in new[] { PrefijoEventos, PrefijoMetricas })
            {
                foreach (var txt in Directory.GetFiles(carpeta, $"{prefijo}*.txt"))
                {
                    try
                    {
                        var fecha = ObtenerFecha(txt, prefijo);
                        if (fecha is null || fecha.Value >= hoy)
                            continue;

                        var zip = Path.Combine(carpeta, $"{PrefijoZip}{fecha.Value:dd_MM_yyyy}.zip");
                        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
                        {
                            var entrada = Path.GetFileName(txt);
                            archive.GetEntry(entrada)?.Delete();   // evita entradas duplicadas
                            archive.CreateEntryFromFile(txt, entrada);
                        }

                        File.Delete(txt);
                    }
                    catch
                    {
                        // Si un archivo falla, se sigue con los demás
                    }
                }
            }

            // 2. Retención: eliminar los .zip más antiguos que DiasRetencion
            if (DiasRetencion <= 0)
                return;

            var limite = hoy.AddDays(-DiasRetencion);
            foreach (var zip in Directory.GetFiles(carpeta, $"{PrefijoZip}*.zip"))
            {
                try
                {
                    var fecha = ObtenerFecha(zip, PrefijoZip);
                    if (fecha is not null && fecha.Value < limite)
                        File.Delete(zip);
                }
                catch
                {
                    // Se reintenta en el próximo mantenimiento
                }
            }
        }

        // "LOG_EVENT_31_10_2026.txt" / "LOG_31_10_2026.zip" → 31/10/2026
        private static DateTime? ObtenerFecha(string ruta, string prefijo)
        {
            var nombre = Path.GetFileNameWithoutExtension(ruta);
            if (!nombre.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase))
                return null;

            return DateTime.TryParseExact(nombre[prefijo.Length..], FormatoFechaArchivo,
                                          CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
                ? fecha
                : null;
        }

        // ------------------------------------------------------------------
        // Utilidades
        // ------------------------------------------------------------------

        public static string ObtenerIPLocal()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());

                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ip) &&
                        !ip.ToString().StartsWith("169."))
                    {
                        return ip.ToString();
                    }
                }

                return "Sin IP válida";
            }
            catch
            {
                return "Error al obtener IP";
            }
        }
    }
}