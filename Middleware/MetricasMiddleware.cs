using System.Diagnostics;
using TrafficControl.Tasas.Api.Utils;

namespace TrafficControl.Tasas.Api.Middleware
{
    /// <summary>Mide cada petición (endpoint, código HTTP, tiempo) para clsMetricas.</summary>
    public class MetricasMiddleware
    {
        private readonly RequestDelegate _next;

        public MetricasMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var sw = Stopwatch.StartNew();
            var fallo = false;

            try
            {
                await _next(context);
            }
            catch
            {
                fallo = true;   // la atrapa ExceptionMiddleware y responde 500
                throw;
            }
            finally
            {
                sw.Stop();

                // Plantilla de la ruta (api/v1/tasas/{codigoTasa}/estado) para no
                // abrir una métrica distinta por cada código de tasa
                var ruta = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
                var endpoint = $"{context.Request.Method} /{(ruta ?? "(sin ruta)").TrimStart('/')}";
                var codigo = fallo ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;

                clsMetricas.Registrar(endpoint, codigo, sw.ElapsedMilliseconds);
            }
        }
    }
}