using Microsoft.Data.SqlClient;

namespace TrafficControl.Tasas.Api.Utils
{
    public static class ErroresBd
    {
        // Errores de SQL Server que indican que no hay conexión
        private static readonly HashSet<int> NumerosConexion = new()
        {
            -2,      // Timeout
            2, 53,   // Servidor no encontrado / no accesible
            64, 121, 233, 1225, 10053, 10054, 10060, 11001, // Conexión cortada o rechazada
            4060,    // No se puede abrir la base de datos
            18456    // Login fallido
        };

        public static bool EsErrorConexion(Exception? ex)
        {
            for (var e = ex; e is not null; e = e.InnerException)
            {
                if (e is SqlException sql && (NumerosConexion.Contains(sql.Number) || sql.Class >= 20))
                    return true;

                if (e is TimeoutException)
                    return true;
            }
            return false;
        }

        public static string Tipo(Exception ex)
            => EsErrorConexion(ex) ? "SIN CONEXIÓN A BASE DE DATOS" : "ERROR NO CONTROLADO";
    }
}