using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TrafficControl.Tasas.Api.Dtos.Response;
using TrafficControl.Tasas.Api.Services.Interfaces;
using TrafficControl.Tasas.Api.Utils;

namespace TrafficControl.Tasas.Api.Middleware
{
    /// <summary>Atrapa cualquier excepción no controlada y responde en formato estándar.</summary>
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;
        private readonly SeguridadOptions _seguridad;
        private readonly JsonSerializerOptions _json;

        public ExceptionMiddleware(RequestDelegate next,
                                   ILogger<ExceptionMiddleware> logger,
                                   SeguridadOptions seguridad,
                                   IOptions<Microsoft.AspNetCore.Mvc.JsonOptions> jsonOptions)
        {
            _next = next;
            _logger = logger;
            _seguridad = seguridad;
            _json = jsonOptions.Value.JsonSerializerOptions;
        }

        // ILogService es Scoped: se inyecta por petición en InvokeAsync, no en el constructor
        public async Task InvokeAsync(HttpContext context, ILogService log)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                await _next(context);
            }
            catch (BadHttpRequestException ex) when (!context.Response.HasStarted)
            {
                // Body demasiado grande (413) u otra solicitud mal formada a nivel HTTP
                _logger.LogWarning("Solicitud rechazada ({Status}) Ruta: {Ruta}", ex.StatusCode, context.Request.Path);
                clsLog.GenerandoLog("ExceptionMiddleware",
                $"Solicitud rechazada ({ex.StatusCode}): {ex.Message}. {context.Request.Method} {context.Request.Path} IP: {context.Connection.RemoteIpAddress}",
                NivelLog.Warn);

                var respuesta = new ApiResponse<object>
                {
                    Exito = false,
                    Codigo = ex.StatusCode == StatusCodes.Status413PayloadTooLarge
                        ? Constantes.Codigos.SolicitudMuyGrande
                        : Constantes.Codigos.SolicitudInvalida,
                    Mensaje = ex.StatusCode == StatusCodes.Status413PayloadTooLarge
                        ? "El tamaño de la solicitud excede el máximo permitido"
                        : "Solicitud inválida",
                    IdTransaccion = Guid.NewGuid().ToString("N")
                };

                context.Response.Clear();
                context.Response.StatusCode = ex.StatusCode;
                await context.Response.WriteAsJsonAsync(respuesta, _json);
            }            
            catch (Exception ex)
            {
                sw.Stop();
                var idTransaccion = Guid.NewGuid().ToString("N");

                _logger.LogError(ex, "Error no controlado. Ref: {IdTransaccion} Ruta: {Ruta}",
                                 idTransaccion, context.Request.Path);
                clsLog.GenerandoLog("ExceptionMiddleware",
                $"{ErroresBd.Tipo(ex)}. Ref: {idTransaccion}. {context.Request.Method} {context.Request.Path} IP: {context.Connection.RemoteIpAddress}",
                ex);

                if (context.Response.HasStarted)
                    throw;

                var respuesta = new ApiResponse<object>
                {
                    Exito = false,
                    Codigo = Constantes.Codigos.ErrorInterno,
                    Mensaje = $"Error interno del servicio. Referencia: {idTransaccion}",
                    IdTransaccion = idTransaccion,
                    Errores = _seguridad.MostrarDetalleErrores
                        ? new() { ex.GetBaseException().Message }
                        : new()
                };

                await log.RegistrarAsync($"{context.Request.Method} {context.Request.Path}",
                                         null, respuesta, sw.ElapsedMilliseconds, true, ex);   // ← esta línea

                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(respuesta, _json);
            }
        }
    }
}