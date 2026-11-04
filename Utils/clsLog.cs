using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;

namespace TrafficControl.Tasas.Api.Utils
{
    /// <summary>
    /// Log de texto en C:\BIOMETRIC_LOG\{ProjectName}\LOG_EVENT_dd_MM_yyyy.txt
    /// Los días anteriores se comprimen en LOG_dd_MM_yyyy.zip.
    /// Seguro para varias peticiones simultáneas.
    /// </summary>
    public static class clsLog
    {
        public static string ProjectName { get; set; } = "TrafficControl.Tasas.Api";
        public static string LogPath { get; set; } = @"C:\BIOMETRIC_LOG";

        private const string PrefijoArchivo = "LOG_EVENT_";
        private const string FormatoFechaArchivo = "dd_MM_yyyy";

        // Varias peticiones escriben a la vez: solo un hilo toca el archivo
        private static readonly object _bloqueo = new();
        private static DateTime _ultimaCompresion = DateTime.MinValue;

        public static void GenerandoLog(string funcName, string texto, bool isError)
        {
            try
            {
                var ahora = DateTime.Now;
                var appLogFolder = Path.Combine(LogPath, ProjectName);
                var logFilePath = Path.Combine(appLogFolder, $"{PrefijoArchivo}{ahora:dd_MM_yyyy}.txt");
                var linea = $"{(isError ? "ERROR -> " : "LOG -> ")}{ahora:HH:mm:ss.fff} {funcName}: {texto}";

                lock (_bloqueo)
                {
                    Directory.CreateDirectory(appLogFolder);   // no hace nada si ya existe
                    File.AppendAllText(logFilePath, linea + Environment.NewLine);

                    // Comprime los días anteriores una vez al día, no en cada escritura
                    if (_ultimaCompresion != ahora.Date)
                    {
                        ComprimirArchivosAntiguos(appLogFolder, ahora.Date);
                        _ultimaCompresion = ahora.Date;
                    }
                }
            }
            catch
            {
                // El log nunca debe interrumpir la API
            }
        }

        /// <summary>Registra un error con el detalle completo de la excepción.</summary>
        public static void GenerandoLog(string funcName, string texto, Exception ex)
            => GenerandoLog(funcName, $"{texto} | {ex}", true);

        private static void ComprimirArchivosAntiguos(string logFolder, DateTime hoy)
        {
            foreach (var logFile in Directory.GetFiles(logFolder, $"{PrefijoArchivo}*.txt"))
            {
                try
                {
                    var fecha = ObtenerFechaArchivo(logFile);
                    if (fecha is null || fecha.Value >= hoy)
                        continue;

                    var archiveFileName = Path.Combine(logFolder, $"LOG_{fecha.Value:dd_MM_yyyy}.zip");
                    using (var archive = ZipFile.Open(archiveFileName, ZipArchiveMode.Update))
                    {
                        var entryName = Path.GetFileName(logFile);
                        archive.GetEntry(entryName)?.Delete();   // evita entradas duplicadas
                        archive.CreateEntryFromFile(logFile, entryName);
                    }

                    File.Delete(logFile);
                }
                catch
                {
                    // Si un archivo falla, se sigue con los demás
                }
            }
        }

        // "LOG_EVENT_31_10_2026.txt" → 31/10/2026
        private static DateTime? ObtenerFechaArchivo(string ruta)
        {
            var nombre = Path.GetFileNameWithoutExtension(ruta);
            if (!nombre.StartsWith(PrefijoArchivo, StringComparison.OrdinalIgnoreCase))
                return null;

            return DateTime.TryParseExact(nombre[PrefijoArchivo.Length..], FormatoFechaArchivo,
                                          CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
                ? fecha
                : null;
        }

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