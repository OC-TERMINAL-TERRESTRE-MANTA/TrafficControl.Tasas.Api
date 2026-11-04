using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrafficControl.Tasas.Api.Data;
using TrafficControl.Tasas.Api.Dtos.Response;
using TrafficControl.Tasas.Api.Middleware;
using TrafficControl.Tasas.Api.Utils;

namespace TrafficControl.Tasas.Api.Controllers
{
    [ApiController]
    [Route("api/v1/health")]
    [Produces("application/json")]
    public class HealthController : ControllerBase
    {
        private readonly TrafficControlDbContext _db;
        private readonly SeguridadOptions _seguridad;

        public HealthController(TrafficControlDbContext db, SeguridadOptions seguridad)
        {
            _db = db;
            _seguridad = seguridad;
        }

        /// <summary>Señal de vida del API.</summary>
        [HttpGet]
        [SinApiKey]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        public IActionResult Ping()
        {
            var inicio = Process.GetCurrentProcess().StartTime;
            var tiempoActivo = (DateTime.Now - inicio).ToString(@"d\.hh\:mm\:ss");

            clsLog.GenerandoLog("HealthController.Ping",
                $"Señal de vida consultada. IP: {HttpContext.Connection.RemoteIpAddress}. Tiempo activo: {tiempoActivo}",
                false);

            return Ok(new ApiResponse<object>
            {
                Exito = true,
                Codigo = Constantes.Codigos.ApiOk,
                Mensaje = "TrafficControl.Tasas.Api en línea",
                IdTransaccion = Guid.NewGuid().ToString("N"),
                Datos = new
                {
                    Servicio = "TrafficControl.Tasas.Api",
                    Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                    Servidor = Environment.MachineName,
                    Inicio = inicio,
                    TiempoActivo = tiempoActivo
                }
            });
        }

        /// <summary>Prueba la conexión a la base de datos.</summary>
        [HttpGet("db")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> TestDb(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var ip = HttpContext.Connection.RemoteIpAddress;
            var respuesta = new ApiResponse<object> { IdTransaccion = Guid.NewGuid().ToString("N") };

            try
            {
                // Abre la conexión de verdad para obtener el motivo exacto si falla
                await _db.Database.OpenConnectionAsync(ct);
                await _db.Database.CloseConnectionAsync();
                sw.Stop();

                respuesta.Exito = true;
                respuesta.Codigo = Constantes.Codigos.BdOk;
                respuesta.Mensaje = "Conexión a la base de datos correcta";

                clsLog.GenerandoLog("HealthController.TestDb",
                    $"Conexión a BD correcta ({sw.ElapsedMilliseconds} ms). IP: {ip}", false);
            }
            catch (Exception ex)
            {
                sw.Stop();
                respuesta.Exito = false;
                respuesta.Codigo = Constantes.Codigos.BdError;
                respuesta.Mensaje = "No se pudo conectar a la base de datos";
                if (_seguridad.MostrarDetalleErrores)
                    respuesta.Errores.Add(ex.GetBaseException().Message);

                clsLog.GenerandoLog("HealthController.TestDb",
                    $"SIN CONEXIÓN A BASE DE DATOS ({sw.ElapsedMilliseconds} ms). IP: {ip}", ex);
            }

            respuesta.Datos = new
            {
                BaseDatos = _db.Database.GetDbConnection().Database,
                TiempoRespuesta = $"{sw.ElapsedMilliseconds} ms"
            };

            return StatusCode(respuesta.Exito ? StatusCodes.Status200OK
                                              : StatusCodes.Status503ServiceUnavailable, respuesta);
        }
    }
}