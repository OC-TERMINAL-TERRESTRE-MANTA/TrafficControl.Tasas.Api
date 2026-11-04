using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TrafficControl.Tasas.Api.Dtos;
using TrafficControl.Tasas.Api.Dtos.Response;
using TrafficControl.Tasas.Api.Services.Interfaces;
using TrafficControl.Tasas.Api.Utils;

namespace TrafficControl.Tasas.Api.Middleware
{
    public class ApiKeyMiddleware
    {
        public const string ClienteInfoKey = "ClienteInfo";

        private readonly RequestDelegate _next;
        private readonly ILogger<ApiKeyMiddleware> _logger;
        private readonly IMemoryCache _cache;
        private readonly string _headerName;
        private readonly byte[][] _hashKeys;
        private readonly HashSet<IPAddress> _ipsPermitidas;
        private readonly int _maxIntentos;
        private readonly TimeSpan _tiempoBloqueo;
        private readonly JsonSerializerOptions _json;

        public ApiKeyMiddleware(RequestDelegate next,
                                SeguridadOptions seguridad,
                                ILogger<ApiKeyMiddleware> logger,
                                IMemoryCache cache,
                                IOptions<Microsoft.AspNetCore.Mvc.JsonOptions> jsonOptions)
        {
            _next = next;
            _logger = logger;
            _cache = cache;
            _headerName = seguridad.ApiKeyHeader;
            _hashKeys = seguridad.ApiKeys.Select(Hash).ToArray();
            _ipsPermitidas = seguridad.IpsPermitidas.Select(IPAddress.Parse).ToHashSet();
            _maxIntentos = seguridad.MaxIntentosFallidos;
            _tiempoBloqueo = TimeSpan.FromMinutes(seguridad.MinutosBloqueo);
            _json = jsonOptions.Value.JsonSerializerOptions;
        }

        public async Task InvokeAsync(HttpContext context, ILogService log)
        {
            var ip = NormalizarIp(context.Connection.RemoteIpAddress);
            var claveFallos = $"fallos:{ip?.ToString() ?? "sin-ip"}";

            context.Items[ClienteInfoKey] = new ClienteInfo(
                ip?.ToString(),
                context.Request.Host.ToString(),
                context.Request.Headers.UserAgent.ToString(),
                context.Request.Method,
                context.Request.Path.ToString());

            if (EsPublico(context))
            {
                await _next(context);
                return;
            }

            // 1. Lista blanca de IPs (vacía = todas permitidas)
            if (_ipsPermitidas.Count > 0 && (ip is null || !_ipsPermitidas.Contains(ip)))
            {
                _logger.LogWarning("IP no permitida: {Ip} Ruta: {Ruta}", ip, context.Request.Path);
                await RechazarAsync(context, log, StatusCodes.Status403Forbidden,
                                    Constantes.Codigos.IpNoPermitida, "Origen no autorizado");
                return;
            }

            // 2. IP bloqueada por intentos fallidos → no se registra en BD (evita inundar FC_LOGSERVICE)
            if (_cache.TryGetValue(claveFallos, out int fallos) && fallos >= _maxIntentos)
            {
                await RechazarAsync(context, null, StatusCodes.Status429TooManyRequests,
                                    Constantes.Codigos.IpBloqueada,
                                    "Demasiados intentos fallidos. Intente más tarde");
                return;
            }

            // 3. API Key
            if (!context.Request.Headers.TryGetValue(_headerName, out var valor) ||
                string.IsNullOrWhiteSpace(valor.ToString()))
            {
                await RegistrarFalloAsync(context, log, claveFallos, ip,
                                          Constantes.Codigos.ApiKeyRequerida, $"Debe enviar la cabecera {_headerName}");
                return;
            }

            if (!EsValida(valor.ToString()))
            {
                await RegistrarFalloAsync(context, log, claveFallos, ip,
                                          Constantes.Codigos.ApiKeyInvalida, "API Key inválida");
                return;
            }

            _cache.Remove(claveFallos); // key correcta: reinicia el contador
            await _next(context);
        }

        private async Task RegistrarFalloAsync(HttpContext context, ILogService log, string claveFallos,
                                               IPAddress? ip, string codigo, string mensaje)
        {
            var fallos = (_cache.TryGetValue(claveFallos, out int actual) ? actual : 0) + 1;
            _cache.Set(claveFallos, fallos, _tiempoBloqueo);

            _logger.LogWarning("{Codigo}. IP: {Ip} Ruta: {Ruta} Intento: {Fallos}/{Max}",
                               codigo, ip, context.Request.Path, fallos, _maxIntentos);

            if (fallos >= _maxIntentos)
            {
                _logger.LogWarning("IP {Ip} bloqueada {Minutos} min por intentos fallidos", ip, _tiempoBloqueo.TotalMinutes);
                await RechazarAsync(context, log, StatusCodes.Status429TooManyRequests,
                                    Constantes.Codigos.IpBloqueada,
                                    "Demasiados intentos fallidos. Intente más tarde");
                return;
            }

            await RechazarAsync(context, log, StatusCodes.Status401Unauthorized, codigo, mensaje);
        }

        private static bool EsPublico(HttpContext context)
        {
            if (context.GetEndpoint()?.Metadata.GetMetadata<SinApiKeyAttribute>() is not null)
                return true;

            return context.Request.Path.StartsWithSegments("/swagger");
        }

        private bool EsValida(string recibida)
        {
            var hashRecibida = Hash(recibida);
            var valida = false;

            foreach (var hashKey in _hashKeys)
                valida |= CryptographicOperations.FixedTimeEquals(hashRecibida, hashKey);

            return valida;
        }

        private static byte[] Hash(string valor) => SHA256.HashData(Encoding.UTF8.GetBytes(valor));

        private static IPAddress? NormalizarIp(IPAddress? ip)
            => ip is not null && ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

        // log = null → no se registra en FC_LOGSERVICE
        private async Task RechazarAsync(HttpContext context, ILogService? log, int status,
                                         string codigo, string mensaje)
        {
            var respuesta = new ApiResponse<object>
            {
                Exito = false,
                Codigo = codigo,
                Mensaje = mensaje,
                IdTransaccion = Guid.NewGuid().ToString("N")
            };

            if (log is not null)
            {
                clsLog.GenerandoLog("Seguridad",
                $"{codigo} ({status}). IP: {context.Connection.RemoteIpAddress}. {context.Request.Method} {context.Request.Path}",
                NivelLog.Warn);

                await log.RegistrarAsync($"{Constantes.FuncionSeguridad} {context.Request.Method} {context.Request.Path}",
                                         null, respuesta, 0, true);
            }

            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(respuesta, _json);
        }
    }
}