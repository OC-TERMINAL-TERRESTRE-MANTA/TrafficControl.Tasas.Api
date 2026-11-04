using Microsoft.Extensions.Options;
using System.Text.Json;
using TrafficControl.Tasas.Api.Data;
using TrafficControl.Tasas.Api.Middleware;
using TrafficControl.Tasas.Api.Models;
using TrafficControl.Tasas.Api.Services.Interfaces;
using TrafficControl.Tasas.Api.Utils;

namespace TrafficControl.Tasas.Api.Services
{
    public class LogService : ILogService
    {
        private readonly TrafficControlDbContext _db;
        private readonly ILogger<LogService> _logger;
        private readonly IHttpContextAccessor _http;
        private readonly JsonSerializerOptions _json;

        public LogService(TrafficControlDbContext db, ILogger<LogService> logger,
                          IHttpContextAccessor http,
                          IOptions<Microsoft.AspNetCore.Mvc.JsonOptions> jsonOptions)
        {
            _db = db;
            _logger = logger;
            _http = http;
            _json = jsonOptions.Value.JsonSerializerOptions;
        }

        public async Task RegistrarAsync(string funcion, object? request, object? response, long milisegundos,
                                  bool error, Exception? excepcion = null)
        {
            string param, mensaje;
            try
            {
                param = JsonSerializer.Serialize(new
                {
                    Cliente = _http.HttpContext?.Items[ApiKeyMiddleware.ClienteInfoKey],
                    Request = request
                }, _json);

                object? cuerpo = excepcion is null
                    ? response
                    : new { Respuesta = response, Excepcion = excepcion.ToString() };
                mensaje = JsonSerializer.Serialize(cuerpo, _json);
            }
            catch (Exception ex)
            {
                clsLog.GenerandoLog($"LogService.{funcion}", "No se pudo serializar el registro de auditoría", ex);
                return;
            }

            try
            {
                _db.ChangeTracker.Clear();

                _db.FC_LOGSERVICE.Add(new FC_LOGSERVICE
                {
                    LOGSERVICE_FUNCION = funcion.Length > 100 ? funcion[..100] : funcion,
                    LOGSERVICE_PARAM = param,
                    LOGSERVICE_MENSAJE = mensaje,
                    LOGSERVIE_FECHA = DateTime.Now,
                    LOGSERVICE_TIME = $"{milisegundos} ms",
                    LOGSERVICE_ERROR = error
                });

                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo registrar en FC_LOGSERVICE ({Funcion})", funcion);

                // Respaldo: el registro que no entró a FC_LOGSERVICE queda completo en el log de texto
                clsLog.GenerandoLog($"LogService.{funcion}",
                    $"{ErroresBd.Tipo(ex)} al guardar en FC_LOGSERVICE: {ex.GetBaseException().Message} | " +
                    $"TIEMPO={milisegundos} ms | ERROR={error} | PARAM={param} | MENSAJE={mensaje}",
                    true);
            }
        }
    }
}