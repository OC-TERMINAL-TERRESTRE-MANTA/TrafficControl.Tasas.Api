using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using ParkingControl.Facturacion.Worker.Criptografia;
using System.Threading.RateLimiting;
using TrafficControl.Tasas.Api.Data;
using TrafficControl.Tasas.Api.Dtos.Response;
using TrafficControl.Tasas.Api.Middleware;
using TrafficControl.Tasas.Api.Services;
using TrafficControl.Tasas.Api.Services.Interfaces;
using TrafficControl.Tasas.Api.Utils;

// Un servicio de Windows arranca en C:\Windows\System32. Sin esto no encuentra appsettings.json.
var esServicio = WindowsServiceHelpers.IsWindowsService();

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = esServicio ? AppContext.BaseDirectory : default
});

// Si se ejecuta desde Visual Studio, la consola o IIS, esto no hace nada.
// Si corre como servicio, se integra con el Service Control Manager y el Visor de eventos.
builder.Host.UseWindowsService(o => o.ServiceName = "TrafficControl.Tasas.Api");

// --- Observabilidad: logs y métricas ---
var observabilidad = builder.Configuration.GetSection("Observabilidad").Get<ObservabilidadOptions>()
                     ?? new ObservabilidadOptions();
clsLog.LogPath = observabilidad.RutaLog;
clsLog.DiasRetencion = observabilidad.DiasRetencion;
builder.Services.AddSingleton(observabilidad);
builder.Services.AddHostedService<MetricasWorker>();

// Puerto solo en modo servicio. En IIS lo define el binding del sitio.
if (esServicio)
{
    var url = builder.Configuration["ServicioWindows:Url"] ?? "http://0.0.0.0:5080";
    builder.WebHost.UseUrls(url);
}

// ---------------- Cadena de conexión cifrada ----------------
var cadenaCifrada = builder.Configuration.GetConnectionString("CadenaTasas")!;
string cadena;
try
{
    cadena = clsCriptografiaModerna.Desencriptar(cadenaCifrada);
    if (string.IsNullOrWhiteSpace(cadena))
        throw new InvalidOperationException("La cadena descifrada está vacía");
}
catch (Exception ex)
{
    clsLog.GenerandoLog("Program", "No se pudo descifrar ConnectionStrings:CadenaTasas", ex);
    throw;
}

builder.Services.AddDbContextPool<TrafficControlDbContext>(o => o.UseSqlServer(cadena));
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();                    // ★ requerido por ApiKeyMiddleware (bloqueo por intentos)

// ---------------- Seguridad: API Keys cifradas ----------------
//var seguridad = builder.Configuration.GetSection("Seguridad").Get<SeguridadOptions>() ?? new SeguridadOptions();
//try
//{
//    seguridad.ApiKeys = seguridad.ApiKeys.Select(k => clsCriptografiaModerna.Desencriptar(k)).ToList();
//}
//catch (Exception ex)
//{
//    throw new InvalidOperationException("No se pudieron descifrar las API Keys de Seguridad:ApiKeys.", ex);
//}

//if (seguridad.ApiKeys.Count == 0)
//    throw new InvalidOperationException("No hay API Keys configuradas en Seguridad:ApiKeys");

//builder.Services.AddSingleton(seguridad);
var seguridad = builder.Configuration.GetSection("Seguridad").Get<SeguridadOptions>() ?? new SeguridadOptions();

// API Keys en texto plano: se comparan tal cual llegan en la cabecera X-API-KEY
seguridad.ApiKeys = seguridad.ApiKeys
    .Where(k => !string.IsNullOrWhiteSpace(k))
    .Select(k => k.Trim())
    .ToList();

if (seguridad.ApiKeys.Count == 0)
{
    clsLog.GenerandoLog("Program", "No hay API Keys configuradas en Seguridad:ApiKeys. La API no arranca.", true);
    throw new InvalidOperationException("No hay API Keys configuradas en Seguridad:ApiKeys");
}

builder.Services.AddSingleton(seguridad);

// ---------------- Tamaño máximo del body (Kestrel = servicio, IIS = sitio) ----------------
var maxBody = seguridad.MaxTamanoBodyKb * 1024L;
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = maxBody;
    o.AddServerHeader = false;
});
builder.Services.Configure<IISServerOptions>(o => o.MaxRequestBodySize = maxBody);

// ---------------- Límite de peticiones por IP ----------------
builder.Services.AddRateLimiter(o =>
{
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = seguridad.PeticionesPorSegundoPorIp,
                Window = TimeSpan.FromSeconds(1),
                QueueLimit = 0
            }));

    o.OnRejected = async (ctx, ct) =>
    {
        var json = ctx.HttpContext.RequestServices
            .GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;

        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ctx.HttpContext.Response.Headers.RetryAfter = "1";

        await ctx.HttpContext.Response.WriteAsJsonAsync(new ApiResponse<object>
        {
            Exito = false,
            Codigo = Constantes.Codigos.LimitePeticiones,
            Mensaje = "Se excedió el límite de peticiones. Reintente en 1 segundo",
            IdTransaccion = Guid.NewGuid().ToString("N")
        }, json, ct);
    };
});

// ---------------- Servicios ----------------
builder.Services.AddScoped<ILogService, LogService>();
builder.Services.AddScoped<ITasaService, TasaService>();

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new DateTimeIsoConverter()))
    .ConfigureApiBehaviorOptions(o =>
    {
        o.InvalidModelStateResponseFactory = ctx =>
        {
            // En producción no se muestran los mensajes internos de .NET
            var errores = ctx.ModelState
                .Where(x => x.Value!.Errors.Count > 0)
                .SelectMany(x => x.Value!.Errors.Select(err =>
                    seguridad.MostrarDetalleErrores && !string.IsNullOrEmpty(err.ErrorMessage)
                        ? $"{x.Key}: {err.ErrorMessage}"
                        : $"{x.Key}: valor inválido o con formato incorrecto"))
                .ToList();

            return new BadRequestObjectResult(new ApiResponse<object>
            {
                Exito = false,
                Codigo = Constantes.Codigos.ValidacionError,
                Mensaje = "El JSON enviado no tiene un formato válido",
                IdTransaccion = Guid.NewGuid().ToString("N"),
                Errores = errores
            });
        };
    });

// ---------------- Swagger con API Key ----------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "TrafficControl.Tasas.Api", Version = "v1" });

    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Name = seguridad.ApiKeyHeader,
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Description = "API Key asignada al ERP Olympo"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" }
            },
            Array.Empty<string>()
        }
    });
});



// ---------------- Pipeline ----------------
var app = builder.Build();

app.Lifetime.ApplicationStarted.Register(() =>
{
    clsLog.GenerandoLog("Program",
        $"API iniciada. Modo: {(esServicio ? "Servicio de Windows" : "IIS / Consola")}. " +
        $"Servidor: {Environment.MachineName}. IP local: {clsLog.ObtenerIPLocal()}. Ruta: {AppContext.BaseDirectory}",
        false);

    // Verifica la BD al arrancar sin retrasar el inicio
    _ = Task.Run(async () =>
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TrafficControlDbContext>();
            await db.Database.OpenConnectionAsync();
            await db.Database.CloseConnectionAsync();
            clsLog.GenerandoLog("Program", "Conexión a la base de datos verificada al iniciar", false);
        }
        catch (Exception ex)
        {
            clsLog.GenerandoLog("Program", "SIN CONEXIÓN A BASE DE DATOS al iniciar la API", ex);
        }
    });
});

app.Lifetime.ApplicationStopping.Register(() =>
    clsLog.GenerandoLog("Program", "API deteniéndose (reciclaje del pool, parada del servicio o apagado)", false));


app.Logger.LogInformation("TrafficControl.Tasas.Api iniciado. Modo: {Modo}. Ruta: {Ruta}",
    esServicio ? "Servicio de Windows" : "Consola/IIS", AppContext.BaseDirectory);
app.Use(async (ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        ctx.Response.Headers.XContentTypeOptions = "nosniff";
        ctx.Response.Headers.CacheControl = "no-store";
        return Task.CompletedTask;
    });
    await next();
});

app.UseMiddleware<ExceptionMiddleware>();   // 1. primero: envuelve todo el pipeline

if (app.Configuration.GetValue<bool>("HabilitarSwagger"))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("swagger/v1/swagger.json", "TrafficControl.Tasas.Api v1");
        c.RoutePrefix = string.Empty;   // Swagger en la raíz: http://localhost:8085/
        c.DocumentTitle = "TrafficControl.Tasas.Api";
    });
}

// app.UseHttpsRedirection();  // actívalo solo si configuras HTTPS con certificado

app.UseRouting();                           // 2. necesario para que el middleware lea [SinApiKey]
app.UseMiddleware<MetricasMiddleware>();   // ← nuevo
app.UseRateLimiter();                       // 3. corta el exceso antes de tocar la BD
app.UseMiddleware<ApiKeyMiddleware>();      // 4. IP permitida + bloqueo + API Key
app.MapControllers();

try
{
    app.Run();
}
catch (Exception ex)
{
    clsLog.GenerandoLog("Program", "La API se detuvo por un error", ex);
    throw;
}