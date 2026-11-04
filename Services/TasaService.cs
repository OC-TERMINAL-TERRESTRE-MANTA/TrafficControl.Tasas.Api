using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using TrafficControl.Tasas.Api.Data;
using TrafficControl.Tasas.Api.Dtos.Request;
using TrafficControl.Tasas.Api.Dtos.Response;
using TrafficControl.Tasas.Api.Models;
using TrafficControl.Tasas.Api.Services.Interfaces;
using TrafficControl.Tasas.Api.Utils;

namespace TrafficControl.Tasas.Api.Services
{
    public class TasaService : ITasaService
    {
        private readonly TrafficControlDbContext _db;
        private readonly ILogService _log;
        private readonly ILogger<TasaService> _logger;
        private readonly SeguridadOptions _seguridad;
        // Operaciones que tardan más que esto quedan en el log de texto (indicio de BD lenta)
        private const long MsOperacionLenta = 3000;

        public TasaService(TrafficControlDbContext db, ILogService log, ILogger<TasaService> logger,
                           SeguridadOptions seguridad)
        {
            _db = db;
            _log = log;
            _logger = logger;
            _seguridad = seguridad;
        }

        // =====================================================================
        // MÉTODOS PÚBLICOS
        // =====================================================================

        public Task<ApiResponse<TasaResponseDto>> InsertarTasaAsync(TasaRequestDto request)
            => EjecutarAsync(Constantes.FuncionInsertarTasa, request,
                             () => ProcesarInsercionAsync(request),
                             "Error interno al procesar la tasa");

        public Task<ApiResponse<TasaEstadoResponseDto>> ConsultarEstadoAsync(string codigoTasa)
            => EjecutarAsync(Constantes.FuncionConsultarEstadoTasa, new { CodigoTasa = codigoTasa },
                             () => ProcesarConsultaAsync(codigoTasa),
                             "Error interno al consultar la tasa");

        // =====================================================================
        // ENVOLTORIO COMÚN: tiempo, manejo de errores y log en FC_LOGSERVICE
        // =====================================================================        

        private async Task<ApiResponse<T>> EjecutarAsync<T>(string funcion, object? request,
                                                            Func<Task<ApiResponse<T>>> proceso,
                                                            string mensajeError)
        {
            var sw = Stopwatch.StartNew();
            ApiResponse<T> respuesta;
            Exception? excepcion = null;

            try
            {
                respuesta = await proceso();
            }
            catch (Exception ex)
            {
                excepcion = ex;

                var referencia = Guid.NewGuid().ToString("N");
                _logger.LogError(ex, "{Funcion} falló. Ref: {Referencia}", funcion, referencia);
                clsLog.GenerandoLog($"TasaService.{funcion}",
                    $"{ErroresBd.Tipo(ex)}. Ref: {referencia}. Request: {Serializar(request)}", ex);

                respuesta = Fallo<T>(StatusCodes.Status500InternalServerError,
                                     Constantes.Codigos.ErrorInterno,
                                     $"{mensajeError}. Referencia: {referencia}",
                                     _seguridad.MostrarDetalleErrores ? new() { ex.GetBaseException().Message } : null);
            }

            sw.Stop();

            if (sw.ElapsedMilliseconds >= MsOperacionLenta)
                clsLog.GenerandoLog($"TasaService.{funcion}",
                    $"Operación lenta: {sw.ElapsedMilliseconds} ms. Código: {respuesta.Codigo}. Request: {Serializar(request)}",
                    NivelLog.Warn);

            respuesta.FechaRespuesta = DateTime.Now;
            await _log.RegistrarAsync(funcion, request, respuesta, sw.ElapsedMilliseconds, !respuesta.Exito, excepcion);
            return respuesta;
        }

        private static string Serializar(object? valor)
        {
            try { return JsonSerializer.Serialize(valor); }
            catch { return "(no serializable)"; }
        }

        // =====================================================================
        // INSERTAR TASA
        // =====================================================================

        private async Task<ApiResponse<TasaResponseDto>> ProcesarInsercionAsync(TasaRequestDto request)
        {
            var (errores, datos) = Validar(request);
            if (errores.Count > 0)
                return Fallo<TasaResponseDto>(StatusCodes.Status400BadRequest,
                                              Constantes.Codigos.ValidacionError,
                                              "La tasa contiene errores de validación", errores);

            var codigo = request.CodigoTasa!.Trim();

            if (await ExisteTasaAsync(codigo))
                return Fallo<TasaResponseDto>(StatusCodes.Status409Conflict,
                                              Constantes.Codigos.TasaDuplicada,
                                              $"La tasa {codigo} ya se encuentra registrada");

            var tasa = new PC_TASA
            {
                TS_IDBC = codigo,
                TS_VALOR = Math.Round(datos!.Valor, 2),
                TS_FECHA_DESDE = datos.FechaDesde,
                TS_FECHA_HASTA = datos.FechaHasta,
                TS_IDCOOP = datos.IdCooperativa,
                TS_ESTADO = Constantes.EstadoTasaRegistrada,
                TS_FECHACREA = DateTime.Now
            };

            _db.PC_TASA.Add(tasa);

            try
            {
                await _db.SaveChangesAsync();   // aquí EF llena tasa.TS_ID (identity)
            }
            catch (DbUpdateException ex) when (EsDuplicado(ex))
            {
                return Fallo<TasaResponseDto>(StatusCodes.Status409Conflict,
                                              Constantes.Codigos.TasaDuplicada,
                                              $"La tasa {codigo} ya se encuentra registrada");
            }

            return Exito(StatusCodes.Status201Created, Constantes.Codigos.TasaRegistrada,
                         "Tasa registrada correctamente",
                         new TasaResponseDto
                         {
                             IdTasa = tasa.TS_ID,
                             CodigoTasa = tasa.TS_IDBC,
                             ValorTasa = Redondear(tasa.TS_VALOR),
                             FechaDesde = tasa.TS_FECHA_DESDE,
                             FechaHasta = tasa.TS_FECHA_HASTA,
                             IdCooperativa = tasa.TS_IDCOOP,
                             Estado = tasa.TS_ESTADO,
                             DescripcionEstado = DescribirEstado(tasa.TS_ESTADO),
                             FechaRegistro = tasa.TS_FECHACREA
                         });
        }

        private async Task<bool> ExisteTasaAsync(string codigo)
            => await _db.PC_TASA.AsNoTracking().Where(t => t.TS_IDBC == codigo).Select(t => 1)
                .Concat(_db.PC_TASAHIST.AsNoTracking().Where(t => t.TS_IDBC == codigo).Select(t => 1))
                .AnyAsync();

        // =====================================================================
        // CONSULTAR ESTADO
        // =====================================================================

        private async Task<ApiResponse<TasaEstadoResponseDto>> ProcesarConsultaAsync(string codigoTasa)
        {
            if (!CodigoValido(codigoTasa))
                return Fallo<TasaEstadoResponseDto>(StatusCodes.Status400BadRequest,
                                                    Constantes.Codigos.ValidacionError,
                                                    "CodigoTasa debe ser numérico de máximo 20 dígitos");

            var codigo = codigoTasa.Trim();

            // 1. Histórico primero (utilizada = estado definitivo)
            var tasa = await _db.PC_TASAHIST.AsNoTracking()
                .Where(t => t.TS_IDBC == codigo)
                .Select(t => new TasaEstadoResponseDto
                {
                    IdTasa = t.TS_ID,
                    CodigoTasa = t.TS_IDBC,
                    ValorTasa = t.TS_VALOR,
                    FechaDesde = t.TS_FECHA_DESDE,
                    FechaHasta = t.TS_FECHA_HASTA,
                    IdCooperativa = t.TS_IDCOOP,
                    Estado = t.TS_ESTADO,
                    FechaRegistro = t.TS_FECHACREA,
                    FechaUso = t.TS_FECHAUSO
                })
                .FirstOrDefaultAsync();

            // 2. Online
            tasa ??= await _db.PC_TASA.AsNoTracking()
                .Where(t => t.TS_IDBC == codigo)
                .Select(t => new TasaEstadoResponseDto
                {
                    IdTasa = t.TS_ID,
                    CodigoTasa = t.TS_IDBC,
                    ValorTasa = t.TS_VALOR,
                    FechaDesde = t.TS_FECHA_DESDE,
                    FechaHasta = t.TS_FECHA_HASTA,
                    IdCooperativa = t.TS_IDCOOP,
                    Estado = t.TS_ESTADO,
                    FechaRegistro = t.TS_FECHACREA,
                    FechaUso = t.TS_FECHAUSO
                })
                .FirstOrDefaultAsync();

            if (tasa is null)
                return Fallo<TasaEstadoResponseDto>(StatusCodes.Status404NotFound,
                                                    Constantes.Codigos.TasaNoExiste,
                                                    $"La tasa {codigo} no existe");

            tasa.ValorTasa = Redondear(tasa.ValorTasa);
            tasa.Estado = tasa.Estado?.Trim();
            tasa.DescripcionEstado = DescribirEstado(tasa.Estado);

            return Exito(StatusCodes.Status200OK, Constantes.Codigos.TasaEncontrada,
                         $"Tasa {tasa.DescripcionEstado}", tasa);
        }

        private static string DescribirEstado(string? estado) => estado?.Trim() switch
        {
            Constantes.EstadoTasaRegistrada => "NO UTILIZADA",
            Constantes.EstadoTasaUtilizada => "UTILIZADA",
            Constantes.EstadoTasaCaducada => "CADUCADA",
            _ => "DESCONOCIDO"
        };

        // =====================================================================
        // VALIDACIONES
        // =====================================================================

        private sealed record TasaValidada(decimal Valor, DateTime FechaDesde, DateTime FechaViaje,
                                           DateTime FechaHasta, int IdCooperativa);

        private static bool CodigoValido(string? codigo)
            => !string.IsNullOrWhiteSpace(codigo)
               && codigo.Trim().Length <= 20
               && codigo.Trim().All(char.IsAsciiDigit);

        private static (List<string> Errores, TasaValidada? Datos) Validar(TasaRequestDto? r)
        {
            var e = new List<string>();
            if (r is null) return (new() { "El cuerpo de la solicitud es obligatorio." }, null);

            if (string.IsNullOrWhiteSpace(r.CodigoTasa))
                e.Add("CodigoTasa es obligatorio.");
            else if (!CodigoValido(r.CodigoTasa))
                e.Add("CodigoTasa debe ser numérico de máximo 20 dígitos.");

            if (r.ValorTasa is null)
                e.Add("ValorTasa es obligatorio.");
            else if (r.ValorTasa <= 0)
                e.Add("ValorTasa debe ser mayor a 0.");
            else if (decimal.Round(r.ValorTasa.Value, 2) != r.ValorTasa.Value)
                e.Add("ValorTasa admite máximo 2 decimales.");

            var desde = ValidarFecha(r.FechaDesde, "FechaDesde", e);
            var viaje = ValidarFecha(r.FechaViaje, "FechaViaje", e);
            var hasta = ValidarFecha(r.FechaHasta, "FechaHasta", e);

            if (desde.HasValue && hasta.HasValue && desde > hasta)
                e.Add("FechaDesde no puede ser mayor que FechaHasta.");
            if (desde.HasValue && hasta.HasValue && viaje.HasValue && (viaje < desde || viaje > hasta))
                e.Add("FechaViaje debe estar entre FechaDesde y FechaHasta.");

            ValidarTexto(r.IdViaje, "IdViaje", 50, e);
            ValidarTexto(r.IdSilo, "IdSilo", 50, e);
            ValidarTexto(r.Usuario, "Usuario", 20, e);
            ValidarTexto(r.IdCooperativa, "IdCooperativa", 50, e);

            int idCoop = 0;
            if (!string.IsNullOrWhiteSpace(r.IdCooperativa) && !int.TryParse(r.IdCooperativa.Trim(), out idCoop))
                e.Add("IdCooperativa debe ser un valor numérico entero.");

            if (e.Count > 0) return (e, null);

            return (e, new TasaValidada(r.ValorTasa!.Value, desde!.Value, viaje!.Value, hasta!.Value, idCoop));
        }

        private static DateTime? ValidarFecha(string? valor, string campo, List<string> e)
        {
            if (string.IsNullOrWhiteSpace(valor))
            {
                e.Add($"{campo} es obligatorio.");
                return null;
            }

            if (DateTime.TryParseExact(valor.Trim(), Constantes.FormatoFecha, CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out var fecha))
                return fecha;

            e.Add($"{campo} debe tener el formato {Constantes.FormatoFecha}.");
            return null;
        }

        private static void ValidarTexto(string? valor, string campo, int max, List<string> e)
        {
            if (string.IsNullOrWhiteSpace(valor))
                e.Add($"{campo} es obligatorio.");
            else if (valor.Trim().Length > max)
                e.Add($"{campo} excede el máximo de {max} caracteres.");
        }

        // =====================================================================
        // UTILIDADES
        // =====================================================================

        private static decimal? Redondear(decimal? valor)
            => valor.HasValue ? Math.Round(valor.Value, 2) : null;

        private static bool EsDuplicado(DbUpdateException ex)
            => ex.InnerException is SqlException sql && (sql.Number == 2627 || sql.Number == 2601);

        // Éxito: idTransaccion = TS_ID de la tasa
        private static ApiResponse<T> Exito<T>(int http, string codigo, string mensaje, T datos)
            where T : TasaResponseDto => new()
            {
                Exito = true,
                HttpStatus = http,
                Codigo = codigo,
                Mensaje = mensaje,
                IdTransaccion = datos.IdTasa.ToString(),
                Datos = datos
            };

        // Falla: idTransaccion = "0"
        private static ApiResponse<T> Fallo<T>(int http, string codigo, string mensaje,
                                               List<string>? errores = null) => new()
                                               {
                                                   Exito = false,
                                                   HttpStatus = http,
                                                   Codigo = codigo,
                                                   Mensaje = mensaje,
                                                   IdTransaccion = Constantes.IdTransaccionFallida,
                                                   Errores = errores ?? new()
                                               };
    }
}