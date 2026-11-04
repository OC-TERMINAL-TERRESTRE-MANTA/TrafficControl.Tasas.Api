# TrafficControl.Tasas.Api

API REST de **Traffic Control** para recibir, validar, registrar y consultar las **tasas** que generan las cooperativas desde un sistema externo (ERP). Primera implementación: integración con el **ERP Olympo** para la Empresa Pública Municipal Movilidad de Manta-EP (orden de compra IC-EPMMM-2026-039).

> **Documento interno de Only Control S.A.** Describe cómo está construido el servicio, cómo se configura, cómo se prueba y cómo se instala.
> Para el consumidor del servicio existe un documento aparte: *Documentación Técnica – API de Tasas – Traffic Control* (Word, formato OC).

| | |
|---|---|
| **Responsable técnico** | Tnlg. César Cevallos – Líder de proyecto |
| **Gerente de proyecto** | Ing. David Perugachi |
| **Empresa** | ONLY CONTROL S.A. |
| **Versión de la API** | v1 |
| **Última actualización** | 31/10/2026 |

---

## Contenido

1. [Resumen funcional](#1-resumen-funcional)
2. [Stack tecnológico](#2-stack-tecnológico)
3. [Estructura del proyecto](#3-estructura-del-proyecto)
4. [Arquitectura y flujo de una petición](#4-arquitectura-y-flujo-de-una-petición)
5. [Endpoints](#5-endpoints)
6. [Base de datos](#6-base-de-datos)
7. [Configuración (appsettings.json)](#7-configuración-appsettingsjson)
8. [Seguridad](#8-seguridad)
9. [Auditoría (FC_LOGSERVICE)](#9-auditoría-fc_logservice)
10. [Clases y configuraciones especiales](#10-clases-y-configuraciones-especiales)
11. [Desarrollo local](#11-desarrollo-local)
12. [Pruebas](#12-pruebas)
13. [Publicación (compilar para producción)](#13-publicación-compilar-para-producción)
14. [Instalación en IIS](#14-instalación-en-iis)
15. [Instalación como servicio de Windows](#15-instalación-como-servicio-de-windows)
16. [Actualizar una versión instalada](#16-actualizar-una-versión-instalada)
17. [Cifrado de la cadena de conexión](#17-cifrado-de-la-cadena-de-conexión)
18. [Gestión de API Keys](#18-gestión-de-api-keys)
19. [Solución de problemas](#19-solución-de-problemas)
20. [Checklist de producción](#20-checklist-de-producción)
21. [Pendientes y mejoras sugeridas](#21-pendientes-y-mejoras-sugeridas)

---

## 1. Resumen funcional

El servicio expone cuatro endpoints:

| Servicio | Qué hace |
|---|---|
| **Registrar tasa** | Recibe una tasa del ERP, valida campos, formatos y fechas, controla duplicidad (online + histórico), la guarda en `PC_TASA` con estado `0` y devuelve su `TS_ID`. |
| **Consultar estado** | Busca la tasa en `PC_TASAHIST` (utilizada, estado `1`) y luego en `PC_TASA` (no utilizada, estado `0`). Devuelve datos y `TS_FECHAUSO`. |
| **Señal de vida** | Indica que la API está en línea. Público, sin API Key. |
| **Prueba de BD** | Verifica la conexión a SQL Server. Requiere API Key. |

Todas las transacciones de negocio y los rechazos de seguridad quedan registrados en `FC_LOGSERVICE` (request, response, IP, tiempo, error).

**Ciclo de vida de una tasa:**

```
ERP ──POST──► PC_TASA (TS_ESTADO = '0', TS_FECHAUSO = NULL)   → "NO UTILIZADA"
                  │
                  │  (Traffic Control la utiliza en el terminal y la mueve)
                  ▼
             PC_TASAHIST (TS_ESTADO = '1', TS_FECHAUSO = fecha)  → "UTILIZADA"
```

> El paso de `PC_TASA` a `PC_TASAHIST` **no lo hace esta API**; lo hace Traffic Control al usar la tasa. La API solo inserta en `PC_TASA` y consulta ambas tablas.

---

## 2. Stack tecnológico

| Componente | Versión / detalle |
|---|---|
| Framework | .NET 8 – ASP.NET Core Web API |
| ORM | Entity Framework Core **8.0.33** (database-first, scaffold) |
| Base de datos | Microsoft SQL Server (base de Traffic Control) |
| Documentación | Swashbuckle.AspNetCore 6.x (Swagger / OpenAPI) |
| Hosting | IIS (in-process, ASP.NET Core Module V2) **o** servicio de Windows (Kestrel) |
| Serialización | System.Text.Json |
| Límite de peticiones | `Microsoft.AspNetCore.RateLimiting` (nativo de .NET 8) |
| Caché (bloqueo por intentos) | `IMemoryCache` |

### Paquetes NuGet

| Paquete | Versión | Uso |
|---|---|---|
| `Microsoft.EntityFrameworkCore.SqlServer` | 8.0.33 | Proveedor SQL Server |
| `Microsoft.EntityFrameworkCore.Tools` | 8.0.33 | `Scaffold-DbContext` en la consola de paquetes |
| `Microsoft.EntityFrameworkCore.Design` | 8.0.33 | Diseño / scaffold |
| `Swashbuckle.AspNetCore` | 6.x | Swagger |
| `Microsoft.Extensions.Hosting.WindowsServices` | 8.0.x | Ejecución como servicio de Windows |

`dotnet-tools.json` registra las herramientas locales de .NET del proyecto (por ejemplo `dotnet-ef`). Para restaurarlas: `dotnet tool restore`.

---

## 3. Estructura del proyecto

```
TrafficControl.Tasas.Api/
├── Controllers/
│   ├── HealthController.cs        GET /api/v1/health  (público)  y  /api/v1/health/db
│   └── TasasController.cs         POST /api/v1/tasas  y  GET /api/v1/tasas/{codigoTasa}/estado
├── Criptografia/
│   └── clsCriptografiaModerna     Descifrado de la cadena de conexión (misma clase del Worker de facturación)
├── Data/
│   └── TrafficControlDbContext.cs DbContext generado por scaffold (sin OnConfiguring)
├── Dtos/
│   ├── ClienteInfo.cs             record con IP, host, user-agent, método y ruta del cliente
│   ├── Request/
│   │   └── TasaRequestDto.cs      Cuerpo del POST (todos los campos nullable, valida el servicio)
│   └── Response/
│       ├── ApiResponse.cs         Respuesta estándar genérica ApiResponse<T>
│       ├── TasaResponseDto.cs     Datos de la tasa (IdTasa con [JsonIgnore])
│       └── TasaEstadoResponseDto.cs  Hereda de TasaResponseDto + FechaUso
├── Middleware/
│   ├── ApiKeyMiddleware.cs        IP permitida, bloqueo por intentos, validación de API Key
│   ├── ExceptionMiddleware.cs     Errores no controlados y 413 → respuesta estándar
│   └── SinApiKeyAttribute.cs      Marca un endpoint como público
├── Models/                        Entidades generadas por scaffold (nombres de BD intactos)
│   ├── FC_LOGSERVICE.cs
│   ├── PC_TASA.cs
│   └── PC_TASAHIST.cs
├── Services/
│   ├── Interfaces/
│   │   ├── ILogService.cs
│   │   └── ITasaService.cs
│   ├── LogService.cs              Inserta en FC_LOGSERVICE
│   └── TasaService.cs             Lógica de negocio: validación, duplicidad, inserción, consulta
├── Utils/
│   ├── Constantes.cs              Estados, códigos de respuesta, nombres de función para el log
│   ├── DateTimeIsoConverter.cs    Fechas JSON como yyyy-MM-ddTHH:mm:ss
│   └── SeguridadOptions.cs        Sección "Seguridad" de appsettings
├── Publicacion/                   Salida de la publicación (NO versionar)
├── Publicacion.zip                Paquete para copiar al servidor (NO versionar)
├── appsettings.json
├── dotnet-tools.json
├── Program.cs                     Configuración de servicios y pipeline
└── TrafficControl.Tasas.Api.http  Casos de prueba para Visual Studio
```

> Agregar `Publicacion/` y `Publicacion.zip` al `.gitignore`. Contienen binarios y un `appsettings.json` con la cadena y la API Key del servidor.

### Responsabilidad de cada capa

| Capa | Regla |
|---|---|
| **Controllers** | Solo reciben la petición, llaman al servicio y devuelven `StatusCode(respuesta.HttpStatus, respuesta)`. Sin lógica. |
| **Services** | Toda la lógica de negocio, el manejo de errores y el log. |
| **Middleware** | Lo transversal: seguridad, errores no controlados. |
| **Data / Models** | Generados por scaffold. No se editan a mano salvo ajustes puntuales (ver §6.3). |
| **Dtos** | Contratos de entrada y salida. Nunca se serializan entidades de EF directamente. |
| **Utils** | Constantes, conversores y opciones. |

---

## 4. Arquitectura y flujo de una petición

### 4.1 Orden del pipeline (Program.cs)

El orden importa. Cada paso puede cortar la petición:

```
Petición HTTP
   │
   ├─ 1. Cabeceras de seguridad (OnStarting: nosniff, no-store)
   ├─ 2. ExceptionMiddleware ............ envuelve todo; 413 y 500 → JSON estándar
   ├─ 3. Swagger / SwaggerUI ............ solo si HabilitarSwagger = true (en la raíz "/")
   ├─ 4. UseRouting ..................... necesario para leer [SinApiKey] del endpoint
   ├─ 5. UseRateLimiter ................. 300 req/s por IP → 429 LIMITE_PETICIONES (sin tocar BD)
   ├─ 6. ApiKeyMiddleware
   │      ├─ guarda ClienteInfo en HttpContext.Items
   │      ├─ endpoint público ([SinApiKey] o /swagger) → continúa
   │      ├─ IP fuera de IpsPermitidas (si la lista no está vacía) → 403
   │      ├─ IP bloqueada por intentos → 429 IP_BLOQUEADA (sin log en BD)
   │      ├─ sin cabecera / key inválida → 401 (+ cuenta intento, + log en BD)
   │      └─ key válida → reinicia contador y continúa
   ├─ 7. MapControllers
   │      ├─ Model binding: JSON mal formado → 400 (InvalidModelStateResponseFactory)
   │      └─ Controller → TasaService
   │             ├─ Validaciones de negocio → 400
   │             ├─ Duplicidad (PC_TASA + PC_TASAHIST) → 409
   │             ├─ INSERT PC_TASA → 201 (TS_ID en idTransaccion)
   │             └─ LogService → INSERT FC_LOGSERVICE
   ▼
Respuesta JSON estándar
```

### 4.2 Program.cs – fragmento de referencia del pipeline

```csharp
var app = builder.Build();

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

app.UseMiddleware<ExceptionMiddleware>();

if (app.Configuration.GetValue<bool>("HabilitarSwagger"))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("swagger/v1/swagger.json", "TrafficControl.Tasas.Api v1");
        c.RoutePrefix = string.Empty;   // Swagger en la raíz
        c.DocumentTitle = "TrafficControl.Tasas.Api";
    });
}

app.UseRouting();
app.UseRateLimiter();
app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();
app.Run();
```

### 4.3 Registro de servicios (resumen)

| Registro | Ciclo de vida | Nota |
|---|---|---|
| `TrafficControlDbContext` | Scoped (`AddDbContext` / `AddDbContextPool`) | Cadena descifrada al arrancar |
| `ITasaService → TasaService` | Scoped | |
| `ILogService → LogService` | Scoped | Usa el mismo DbContext de la petición |
| `SeguridadOptions` | Singleton | Leído de `Seguridad` y validado al arrancar |
| `IMemoryCache` | Singleton | Contador de intentos fallidos |
| `IHttpContextAccessor` | Singleton | LogService obtiene `ClienteInfo` |
| Rate limiter | Global | Partición por IP |
| `AddControllers().AddJsonOptions(...)` | — | Registra `DateTimeIsoConverter` |

---

## 5. Endpoints

| Método | Ruta | API Key | Éxito | Función en log |
|---|---|---|---|---|
| `POST` | `/api/v1/tasas` | Sí | `201 TASA_REGISTRADA` | `InsertarTasa` |
| `GET` | `/api/v1/tasas/{codigoTasa}/estado` | Sí | `200 TASA_ENCONTRADA` | `ConsultarEstadoTasa` |
| `GET` | `/api/v1/health` | No | `200 API_OK` | (no se registra) |
| `GET` | `/api/v1/health/db` | Sí | `200 BD_OK` / `503 BD_ERROR` | (no se registra) |

### 5.1 Respuesta estándar

```json
{
  "exito": true,
  "codigo": "TASA_REGISTRADA",
  "mensaje": "Tasa registrada correctamente",
  "idTransaccion": "15872",
  "fechaRespuesta": "2026-10-31T10:15:03",
  "datos": { },
  "errores": []
}
```

### 5.2 Regla de `idTransaccion`

| Caso | Valor |
|---|---|
| Insertar / consultar tasa con éxito | `TS_ID` de la tasa |
| Insertar / consultar tasa con error (400, 404, 409, 500) | `"0"` |
| Health, rechazos de seguridad, JSON mal formado, 413 | GUID |

En los 500 de `TasaService`, como `idTransaccion` es `"0"`, el `mensaje` lleva `Referencia: <guid>` para ubicar el error en el log.

### 5.3 Request de Registrar tasa

| Campo | Tipo JSON | Regla | Columna destino |
|---|---|---|---|
| `codigoTasa` | string | solo dígitos, ≤ 20, único | `PC_TASA.TS_IDBC` (PK) |
| `valorTasa` | number | > 0, ≤ 2 decimales | `TS_VALOR` (redondeado a 2) |
| `fechaDesde` | string | `yyyy-MM-ddTHH:mm:ss`, ≤ fechaHasta | `TS_FECHA_DESDE` |
| `fechaViaje` | string | entre desde y hasta | solo log |
| `fechaHasta` | string | `yyyy-MM-ddTHH:mm:ss` | `TS_FECHA_HASTA` |
| `idViaje` | string | ≤ 50 | solo log |
| `idCooperativa` | string | ≤ 50, entero | `TS_IDCOOP` |
| `idSilo` | string | ≤ 50 | solo log |
| `usuario` | string | ≤ 20 | solo log |

Valores que asigna el servicio: `TS_ESTADO = "0"`, `TS_FECHACREA = DateTime.Now`. `TS_ID` lo genera la BD (identity).

> **Por qué `fechaViaje`, `idViaje`, `idSilo` y `usuario` solo van al log:** la regla del proyecto fue **no modificar las tablas existentes** de Traffic Control, y `PC_TASA` no tiene columnas para esos datos. Quedan en `LOGSERVICE_PARAM` como parte del JSON del request.

### 5.4 Catálogo de códigos (`Constantes.Codigos`)

| Código | HTTP | Origen |
|---|---|---|
| `TASA_REGISTRADA` | 201 | TasaService |
| `TASA_DUPLICADA` | 409 | TasaService (validación previa o PK 2627/2601) |
| `TASA_ENCONTRADA` | 200 | TasaService |
| `TASA_NO_EXISTE` | 404 | TasaService |
| `VALIDACION_ERROR` | 400 | TasaService / InvalidModelStateResponseFactory |
| `ERROR_INTERNO` | 500 | TasaService / ExceptionMiddleware |
| `API_OK` | 200 | HealthController |
| `BD_OK` / `BD_ERROR` | 200 / 503 | HealthController |
| `API_KEY_REQUERIDA` / `API_KEY_INVALIDA` | 401 | ApiKeyMiddleware |
| `IP_NO_PERMITIDA` | 403 | ApiKeyMiddleware |
| `IP_BLOQUEADA` | 429 | ApiKeyMiddleware |
| `LIMITE_PETICIONES` | 429 | Rate limiter (OnRejected) |
| `SOLICITUD_MUY_GRANDE` / `SOLICITUD_INVALIDA` | 413 / 400 | ExceptionMiddleware (`BadHttpRequestException`) |

---

## 6. Base de datos

### 6.1 Tablas utilizadas

**Regla del proyecto: no se cambian nombres de tablas ni de columnas.**

| Tabla | Uso de la API | Operaciones |
|---|---|---|
| `PC_TASA` | Tasas online (no utilizadas, estado `0`) | SELECT, INSERT |
| `PC_TASAHIST` | Tasas utilizadas (estado `1`, `TS_FECHAUSO` con fecha) | SELECT |
| `FC_LOGSERVICE` | Log de auditoría | INSERT (+ SELECT para leer el identity) |

**PC_TASA / PC_TASAHIST** (misma estructura):

| Columna | Tipo | Nota |
|---|---|---|
| `TS_ID` | int / **bigint** | Identity. En el servidor de Manta es **bigint** → `long` en el modelo |
| `TS_IDBC` | varchar(70) PK | Código de la tasa (`codigoTasa`) |
| `TS_FECHACREA` | datetime | Fecha de registro |
| `TS_FECHAUSO` | datetime | Fecha de uso (solo en histórico) |
| `TS_VALOR` | decimal | Valor (el scaffold lo generó como `decimal?`) |
| `TS_ESTADO` | char(1) | `0` no utilizada, `1` utilizada |
| `TS_FECHA_DESDE` / `TS_FECHA_HASTA` | datetime | Vigencia |
| `TS_IDCOOP` | int | Cooperativa |
| `TS_IDTRANS`, `TS_IP_MARCA`, `TS_FECHAENTRYSTATION`, `TS_FECHAPAYSTATION`, `TS_FECHAEXITSTATION`, `TS_FECHAMAXSALIDA`, `TS_IDENTRYSTATION`, `TS_IDPAYSTATION`, `TS_IDEXITSTATION`, `TS_TIPO_PASE` | — | No los usa la API (los llena Traffic Control) |

**FC_LOGSERVICE:**

| Columna | Tipo | Contenido |
|---|---|---|
| `LOGSERVICE_ID` | bigint PK identity | |
| `LOGSERVICE_FUNCION` | nvarchar(100) | `InsertarTasa`, `ConsultarEstadoTasa`, `SEGURIDAD POST /api/v1/tasas`, `POST /ruta` |
| `LOGSERVICE_MENSAJE` | text | JSON de la respuesta (+ excepción completa si hubo error) |
| `LOGSERVIE_FECHA` | datetime | Fecha del registro (**el nombre de la columna tiene el typo original; no corregir**) |
| `LOGSERVICE_TIME` | varchar(50) | Duración, ej. `125 ms` |
| `LOGSERVICE_PARAM` | text | JSON `{ cliente: {...}, request: {...} }` |
| `LOGSERVICE_ERROR` | bit | 1 si la transacción terminó en error |

### 6.2 Scaffold (database-first)

En la **Consola del Administrador de Paquetes** de Visual Studio:

```powershell
Scaffold-DbContext "Server=SERVIDOR;Database=BASE;User Id=USUARIO;Password=CLAVE;TrustServerCertificate=True" Microsoft.EntityFrameworkCore.SqlServer `
  -OutputDir Models -ContextDir Data -Context TrafficControlDbContext `
  -Tables FC_LOGSERVICE,PC_TASA,PC_TASAHIST `
  -UseDatabaseNames -NoPluralize -NoOnConfiguring -Force
```

| Opción | Por qué |
|---|---|
| `-UseDatabaseNames` | Mantiene los nombres exactos (`PC_TASA.TS_IDBC`) en lugar de `PcTasa.TsIdbc`. |
| `-NoPluralize` | No renombra `PC_TASA` a `PC_TASAs`. |
| `-NoOnConfiguring` | **Obligatorio.** Evita que la cadena quede en texto plano dentro del DbContext. |
| `-Tables` | Solo las 3 tablas que usa la API, no toda la base de Traffic Control. |
| `-Force` | Sobrescribe los archivos existentes. |

La cadena va en texto plano **solo** en el comando; no se guarda en ningún archivo.

### 6.3 Ajustes posteriores al scaffold

Revisar después de cada scaffold:

1. **Hacer el scaffold contra la base del servidor de producción.** La base de desarrollo tenía `TS_ID int` y la de producción `bigint`, y eso provocó `Unable to cast object of type 'System.Int64' to type 'System.Int32'`. Si se hace el scaffold contra otra base, comparar los tipos (ver la consulta en §19).
2. `TS_ID` debe tener `.ValueGeneratedOnAdd()` en `TrafficControlDbContext` para que EF lo lea después del INSERT. Si la columna es identity, el scaffold lo genera solo.
3. Si `TS_ID` es `long`, también debe ser `long` en `TasaResponseDto.IdTasa`.
4. Si alguna tabla tiene **triggers**, EF Core 8 necesita declararlos: `entity.ToTable(tb => tb.HasTrigger("NOMBRE_TRIGGER"));`. Sin eso, el INSERT falla por el `OUTPUT` que usa EF.
5. No se usan migraciones. La base la administra Traffic Control.

### 6.4 Usuario SQL con permisos mínimos

```sql
-- En el servidor SQL
CREATE LOGIN usr_tasas_api WITH PASSWORD = '<clave segura>', CHECK_POLICY = ON;

-- En la base de Traffic Control
USE [<BASE_TRAFFIC_CONTROL>];
CREATE USER usr_tasas_api FOR LOGIN usr_tasas_api;

GRANT SELECT, INSERT ON dbo.PC_TASA       TO usr_tasas_api;
GRANT SELECT         ON dbo.PC_TASAHIST   TO usr_tasas_api;
GRANT SELECT, INSERT ON dbo.FC_LOGSERVICE TO usr_tasas_api;   -- SELECT: EF lee el identity con OUTPUT
```

Nunca `sa` ni `db_owner`.

### 6.5 Consultas útiles

```sql
-- Últimas transacciones
SELECT TOP 50 * FROM FC_LOGSERVICE ORDER BY LOGSERVICE_ID DESC;

-- Solo errores
SELECT TOP 50 * FROM FC_LOGSERVICE WHERE LOGSERVICE_ERROR = 1 ORDER BY LOGSERVICE_ID DESC;

-- Buscar un error 500 por su número de referencia
SELECT * FROM FC_LOGSERVICE WHERE LOGSERVICE_MENSAJE LIKE '%<referencia>%';

-- Rechazos de seguridad
SELECT TOP 50 * FROM FC_LOGSERVICE WHERE LOGSERVICE_FUNCION LIKE 'SEGURIDAD%' ORDER BY LOGSERVICE_ID DESC;

-- Historial de una tasa
SELECT * FROM FC_LOGSERVICE WHERE LOGSERVICE_PARAM LIKE '%20261031000000000002%';

-- Estado de una tasa
SELECT 'ONLINE' AS ORIGEN, * FROM PC_TASA     WHERE TS_IDBC = '20261031000000000002'
UNION ALL
SELECT 'HIST',             * FROM PC_TASAHIST WHERE TS_IDBC = '20261031000000000002';
```

> `LOGSERVICE_MENSAJE` y `LOGSERVICE_PARAM` son `text`: el `LIKE` funciona pero no usa índices. Es aceptable para soporte puntual, no para reportes masivos.

---

## 7. Configuración (appsettings.json)

### 7.1 Archivo completo de referencia

```json
{
  "ConnectionStrings": {
    "CadenaTasas": "<cadena cifrada con CriptografiaModerna.exe>"
  },
  "Seguridad": {
    "ApiKeyHeader": "X-API-KEY",
    "ApiKeys": [
      "<API_KEY en texto plano>"
    ],
    "MostrarDetalleErrores": false,
    "IpsPermitidas": [],
    "PeticionesPorSegundoPorIp": 300,
    "MaxIntentosFallidos": 10,
    "MinutosBloqueo": 15,
    "MaxTamanoBodyKb": 16
  },
  "ServicioWindows": {
    "Url": "http://0.0.0.0:5080"
  },
  "HabilitarSwagger": false,
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

### 7.2 Parámetros

| Parámetro | Default | Descripción |
|---|---|---|
| `ConnectionStrings:CadenaTasas` | — | Cadena a SQL Server **cifrada**. Se descifra en `Program.cs` con `clsCriptografiaModerna.Desencriptar`. |
| `Seguridad:ApiKeyHeader` | `X-API-KEY` | Nombre de la cabecera de autenticación. |
| `Seguridad:ApiKeys` | — | Lista de keys válidas, **en texto plano** (no se cifran). Si está vacía, la API **no arranca**. Admite varias para rotación. |
| `Seguridad:MostrarDetalleErrores` | `false` | `true` agrega `ex.Message` en `errores` de los 500 y el detalle de los errores de JSON. **Solo para desarrollo.** |
| `Seguridad:IpsPermitidas` | `[]` | Lista blanca de IPs (ej. `["10.10.1.25"]`). Vacía = todas permitidas. Una IP mal escrita impide que la API arranque. |
| `Seguridad:PeticionesPorSegundoPorIp` | `300` | Límite de peticiones por IP (ventana fija de 1 s, sin cola). |
| `Seguridad:MaxIntentosFallidos` | `10` | Keys inválidas antes de bloquear la IP. |
| `Seguridad:MinutosBloqueo` | `15` | Duración del bloqueo (se renueva con cada nuevo intento fallido). |
| `Seguridad:MaxTamanoBodyKb` | `16` | Tamaño máximo del body. Se aplica a Kestrel y a IIS. |
| `ServicioWindows:Url` | `http://0.0.0.0:5080` | Dirección y puerto de escucha **solo en modo servicio**. En IIS se ignora (el puerto lo define el sitio). |
| `HabilitarSwagger` | `false` | `true` publica Swagger en la raíz (`/`). |
| `Logging:LogLevel:Microsoft.EntityFrameworkCore` | `Warning` | **No bajar a Information en producción**: EF registraría cada SQL (unas 400 líneas por segundo a 100 req/s). |

### 7.3 Configuración por ambiente

- `appsettings.Development.json` solo se carga con `ASPNETCORE_ENVIRONMENT=Development`, que es lo que pone Visual Studio con F5. Ahí se puede dejar `MostrarDetalleErrores: true` y `HabilitarSwagger: true`.
- En IIS y como servicio, el ambiente por defecto es **Production** y solo se usa `appsettings.json`.
- Para forzar un ambiente en IIS, agregarlo en el `web.config`:
  ```xml
  <aspNetCore ...>
    <environmentVariables>
      <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
    </environmentVariables>
  </aspNetCore>
  ```

---

## 8. Seguridad

### 8.1 API Key – `ApiKeyMiddleware`

- **Cabecera:** se lee la configurada (`X-API-KEY`).
- **Almacenamiento:** las keys se configuran en texto plano. Al arrancar, el middleware guarda en memoria su **hash SHA-256**.
- **Comparación:** hash de la key recibida contra cada hash con `CryptographicOperations.FixedTimeEquals`, recorriendo **todas** las keys sin cortar en la primera coincidencia. Así el tiempo de respuesta no da pistas.
- **Endpoints públicos:** los marcados con `[SinApiKey]` (hoy solo `HealthController.Ping`) y las rutas `/swagger`. Swagger en la raíz lo atiende su propio middleware antes de llegar a este.
- **Rechazos:** responden JSON estándar y se registran en `FC_LOGSERVICE` con función `SEGURIDAD <método> <ruta>`. **La key recibida nunca se registra.**
- **Validación al arrancar** (`Program.cs`): si `ApiKeys` queda vacía, se lanza `InvalidOperationException` y la API no levanta.

### 8.2 Bloqueo por intentos fallidos

- Contador por IP en `IMemoryCache`, con la clave `fallos:<ip>`.
- Cada key ausente o inválida suma 1 y renueva la expiración (`MinutosBloqueo`).
- Al llegar a `MaxIntentosFallidos` → `429 IP_BLOQUEADA`. Solo se registra en BD el intento que provoca el bloqueo; los siguientes se rechazan **sin escribir en BD**, para que no se pueda inundar `FC_LOGSERVICE`.
- Una key correcta reinicia el contador.
- **El contador vive en memoria:** se pierde al reciclar el grupo de aplicaciones o al reiniciar el servicio. Es la forma rápida de desbloquear una IP.

### 8.3 Límite de peticiones

- `AddRateLimiter` con `GlobalLimiter` particionado por `RemoteIpAddress`. Ventana fija de 1 s, `PermitLimit = PeticionesPorSegundoPorIp`, `QueueLimit = 0`.
- Rechazo: `429 LIMITE_PETICIONES` con cabecera `Retry-After: 1`. No toca la BD.
- Va **antes** de `ApiKeyMiddleware` en el pipeline.

### 8.4 Lista blanca de IPs

- `IpsPermitidas` vacía = sin restricción (estado actual).
- Las IPv4 que llegan como IPv6 mapeadas (`::ffff:10.0.0.5`) se normalizan con `MapToIPv4()`.
- Rechazo: `403 IP_NO_PERMITIDA`, registrado en BD.

### 8.5 IP real del cliente y proxies

- Se usa `HttpContext.Connection.RemoteIpAddress`. **No se lee `X-Forwarded-For` directamente**, porque cualquiera puede enviar esa cabecera y falsear su IP.
- En IIS in-process y con NAT o redirección de puertos, `RemoteIpAddress` ya es la IP real.
- **Si se coloca un proxy inverso o un balanceador** delante (IIS ARR, WAF), todas las peticiones llegarán con la IP del proxy: el límite por IP se compartiría entre todos y un bloqueo afectaría a todos. En ese caso hay que configurar `ForwardedHeaders` confiando **solo** en la IP del proxy:
  ```csharp
  builder.Services.Configure<ForwardedHeadersOptions>(o =>
  {
      o.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
      o.KnownProxies.Add(IPAddress.Parse("<IP_DEL_PROXY>"));
  });
  // en el pipeline, al inicio:
  app.UseForwardedHeaders();
  ```

### 8.6 Tamaño del body

```csharp
var maxBody = seguridad.MaxTamanoBodyKb * 1024L;
builder.WebHost.ConfigureKestrel(o => { o.Limits.MaxRequestBodySize = maxBody; o.AddServerHeader = false; });
builder.Services.Configure<IISServerOptions>(o => o.MaxRequestBodySize = maxBody);
```

Si se excede, se lanza `BadHttpRequestException(413)` y `ExceptionMiddleware` la convierte en `413 SOLICITUD_MUY_GRANDE`.

### 8.7 Errores internos

- `MostrarDetalleErrores = false`: el cliente recibe un mensaje genérico con número de referencia, y el detalle completo (`ex.ToString()`, con stack trace) se guarda solo en `FC_LOGSERVICE.LOGSERVICE_MENSAJE`, con la forma `{ respuesta, excepcion }`.
- Los errores de lectura del JSON (que incluyen nombres de tipos .NET) se reemplazan por `"<campo>: valor inválido o con formato incorrecto"`.

### 8.8 Otras medidas

| Medida | Dónde |
|---|---|
| SQL parametrizado (sin SQL injection) | EF Core |
| Sin cabecera `Server` | Kestrel `AddServerHeader = false`; en IIS `removeServerHeader` solo desde Windows Server 2019 (ver §14.8) |
| `X-Content-Type-Options: nosniff`, `Cache-Control: no-store` | Middleware inline en `Program.cs` |
| CORS **deshabilitado** a propósito | Integración servidor a servidor; no se configura `AddCors` |
| Swagger apagado en producción | `HabilitarSwagger = false` |
| `/health/db` protegido | Devuelve el nombre de la BD |

---

## 9. Auditoría (FC_LOGSERVICE)

`LogService.RegistrarAsync(funcion, request, response, milisegundos, error, excepcion?)`:

- `LOGSERVICE_PARAM` → `{ "cliente": ClienteInfo, "request": <request> }`. `ClienteInfo` se toma de `HttpContext.Items["ClienteInfo"]` (lo llena `ApiKeyMiddleware`) a través de `IHttpContextAccessor`.
- `LOGSERVICE_MENSAJE` → JSON de la respuesta; si hubo excepción, `{ "respuesta": ..., "excepcion": "<ex.ToString()>" }`.
- Usa las **mismas `JsonSerializerOptions` de MVC** (`IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>`), así las fechas del log salen igual que en la respuesta HTTP.
- Llama a `ChangeTracker.Clear()` antes de insertar. Si el `INSERT` de la tasa falló (por ejemplo, duplicada), la entidad fallida sigue en el contexto, y sin limpiar EF intentaría guardarla otra vez junto con el log.
- Si el log falla, **no se cae la respuesta al ERP**: el error va a `ILogger`.
- `LOGSERVICE_FUNCION` se trunca a 100 caracteres.

Ejemplo de `LOGSERVICE_PARAM`:

```json
{
  "cliente": { "ip": "192.168.5.20", "host": "192.168.5.2:8085", "userAgent": "...", "metodo": "POST", "ruta": "/api/v1/tasas" },
  "request": { "codigoTasa": "20261031000000000002", "valorTasa": 0.50, "...": "...", "usuario": "olympo" }
}
```

**Qué no se registra:** `/health` y `/health/db` (para no llenar la tabla con el monitoreo), los rechazos del rate limiter y los rechazos posteriores al bloqueo de una IP.

---

## 10. Clases y configuraciones especiales

### 10.1 `TasaService`

- **`EjecutarAsync<T>(funcion, request, proceso, mensajeError)`:** envoltorio común de los dos métodos públicos. Mide el tiempo con `Stopwatch`, atrapa excepciones (500 con referencia GUID en el mensaje), pone `FechaRespuesta` y escribe el log. Los métodos `Procesar*` pueden hacer `return` temprano sin saltarse el log.
- **Duplicidad en dos niveles:**
  1. `ExisteTasaAsync`: una sola consulta `UNION ALL` (`Concat`) sobre `PC_TASA` y `PC_TASAHIST`.
  2. Si dos envíos simultáneos pasan el paso 1, la PK de `TS_IDBC` rechaza el segundo. Se atrapa `DbUpdateException` con `SqlException.Number` 2627 o 2601 y se responde 409.
- **Consulta de estado:** busca **primero en `PC_TASAHIST`**. Si el proceso de Traffic Control deja la tasa un instante en ambas tablas, se responde "utilizada", que es el estado definitivo. Usa proyección directa a DTO (`Select(...)`), sin traer entidades.
- **Validación:** devuelve **todos** los errores juntos. `Validar` retorna `(List<string> Errores, TasaValidada? Datos)`.
- **Redondeo:** `TS_VALOR` se guarda con `Math.Round(valor, 2)`. En la respuesta también se redondea, porque la columna tiene 4 decimales y devolvería `0.5000`.
- **`Exito<T>` / `Fallo<T>`:** el primero pone `IdTransaccion = datos.IdTasa`; el segundo pone `IdTransaccion = "0"`.
- **Estados:** `Constantes.EstadoTasaRegistrada = "0"` y `Constantes.EstadoTasaUtilizada = "1"`. `DescribirEstado` los traduce a `NO UTILIZADA` / `UTILIZADA` / `DESCONOCIDO`.

### 10.2 DTOs

- **`TasaRequestDto`:** todas las propiedades son nullable. Así el `[ApiController]` no rechaza por su cuenta los campos vacíos y es `TasaService` quien valida, de modo que **todo intento queda en el log**. Las fechas llegan como `string` para validar el formato exacto con `DateTime.TryParseExact`.
- **`TasaResponseDto.IdTasa`:** `[JsonIgnore]`; solo sirve para llenar `idTransaccion`.
- **`TasaEstadoResponseDto.FechaUso`:** `[JsonPropertyOrder(1)]`. System.Text.Json serializa primero las propiedades de la clase hija; sin este atributo, `fechaUso` saldría al inicio.
- **`ApiResponse<T>.HttpStatus`:** `[JsonIgnore]`; el controller lo usa en `StatusCode(...)`.

### 10.3 `DateTimeIsoConverter`

Convierte `DateTime` (y `DateTime?`, automáticamente) a `yyyy-MM-ddTHH:mm:ss` con `CultureInfo.InvariantCulture`, sin milisegundos ni zona. Se registra en `AddJsonOptions`. Formato centralizado en `Constantes.FormatoFecha`.

> **Hora local de Ecuador (UTC-05:00), sin offset.** Si algún día la API o el ERP pasan a la nube en otra zona, cambiar `FormatoFecha` a `yyyy-MM-ddTHH:mm:sszzz` y ajustar la validación.

### 10.4 `InvalidModelStateResponseFactory`

Reemplaza el `ProblemDetails` por defecto de ASP.NET por `ApiResponse` (`VALIDACION_ERROR`, GUID). Se activa cuando el JSON no se puede deserializar (ej. texto en `valorTasa`). Esos casos **no llegan a TasaService** y por eso **no quedan en `FC_LOGSERVICE`**.

### 10.5 `[SinApiKey]`

Atributo de clase o método. `ApiKeyMiddleware` lo lee con `context.GetEndpoint()?.Metadata.GetMetadata<SinApiKeyAttribute>()`. Por eso `app.UseRouting()` debe ir **antes** del middleware.

### 10.6 Arranque como servicio o en IIS (`Program.cs`)

```csharp
var esServicio = WindowsServiceHelpers.IsWindowsService();

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // Un servicio arranca en C:\Windows\System32; sin esto no encuentra appsettings.json
    ContentRootPath = esServicio ? AppContext.BaseDirectory : default
});

builder.Host.UseWindowsService(o => o.ServiceName = "TrafficControl.Tasas.Api");

if (esServicio)
{
    var url = builder.Configuration["ServicioWindows:Url"] ?? "http://0.0.0.0:5080";
    builder.WebHost.UseUrls(url);
}
```

- En IIS, `IsWindowsService()` devuelve `false`: `UseWindowsService` no hace nada y el puerto lo define el sitio.
- En consola o con F5 tampoco tiene efecto.

### 10.7 Descifrado de la cadena

```csharp
var cadenaCifrada = builder.Configuration.GetConnectionString("CadenaTasas")!;
var cadena = clsCriptografiaModerna.Desencriptar(cadenaCifrada);
builder.Services.AddDbContext<TrafficControlDbContext>(o => o.UseSqlServer(cadena));
```

Las **API Keys no se descifran**: se usan tal como están en `appsettings`.

---

## 11. Desarrollo local

### 11.1 Requisitos

- Visual Studio 2022 (17.8 o superior) con la carga de trabajo *ASP.NET y desarrollo web*.
- .NET SDK 8.0.x.
- Acceso a un SQL Server con las tablas `PC_TASA`, `PC_TASAHIST` y `FC_LOGSERVICE`.
- `CriptografiaModerna.exe` para cifrar la cadena.

### 11.2 Primer arranque

1. Abrir la solución y restaurar los paquetes NuGet.
2. Cifrar la cadena de conexión de desarrollo y ponerla en `ConnectionStrings:CadenaTasas`.
3. Poner una API Key de pruebas en `Seguridad:ApiKeys`.
4. Crear `appsettings.Development.json` (opcional):
   ```json
   {
     "HabilitarSwagger": true,
     "Seguridad": { "MostrarDetalleErrores": true }
   }
   ```
5. F5. Swagger se abre en la raíz (`http://localhost:5266/`). En `Properties/launchSettings.json` dejar `"launchBrowser": true` y `"launchUrl": ""`.

### 11.3 Convenciones

- Nuevos códigos de respuesta y estados → `Constantes`.
- Nuevos endpoints públicos → `[SinApiKey]`.
- Toda operación de negocio nueva → por `EjecutarAsync` en el servicio, para que quede en el log.
- No serializar entidades de EF: siempre DTO.
- No volver a crear `JsonSerializerOptions` en cada llamada: inyectar `IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>`.

---

## 12. Pruebas

> **Estado actual:** no hay proyecto de pruebas unitarias. Las pruebas son manuales (archivo `.http`, Swagger, cURL) y de integración contra la BD. Ver §21 para la propuesta de tests automatizados.

### 12.1 Archivo `TrafficControl.Tasas.Api.http`

Incluido en el proyecto. En Visual Studio aparece *Enviar solicitud* sobre cada bloque.

```http
@TrafficControl.Tasas.Api_HostAddress = http://localhost:5266
@apiKey = <API_KEY_EN_TEXTO_PLANO>
@codigoTasa = 20261031000000000001
```

- `@apiKey` va **en texto plano**, igual que en `appsettings`.
- Cambiar `@codigoTasa` en cada corrida para que el insert dé 201 y no 409.

### 12.2 Casos de prueba

| N.º | Caso | Esperado |
|---|---|---|
| 1 | `GET /api/v1/health` sin key | 200 `API_OK` |
| 2 | `GET /api/v1/health/db` con key | 200 `BD_OK` |
| 3 | `GET /api/v1/health/db` sin key | 401 `API_KEY_REQUERIDA` |
| 4 | POST tasa válida | 201 `TASA_REGISTRADA`, `idTransaccion` = `TS_ID` |
| 5 | POST misma tasa | 409 `TASA_DUPLICADA`, `idTransaccion` = `"0"` |
| 6 | POST campos vacíos | 400 con 9 errores de obligatoriedad |
| 7 | POST formatos inválidos (letras en código, 3 decimales, fecha sin `T`, cooperativa no numérica, usuario > 20) | 400 con todos los errores |
| 8 | POST `fechaDesde` > `fechaHasta` | 400 |
| 9 | POST valor negativo | 400 |
| 10 | POST texto en `valorTasa` | 400 `El JSON enviado no tiene un formato válido` (GUID) |
| 11 | POST sin key | 401 `API_KEY_REQUERIDA` |
| 12 | POST key inválida | 401 `API_KEY_INVALIDA` |
| 13 | 10 keys inválidas seguidas | 429 `IP_BLOQUEADA` (reiniciar la API o reciclar el pool para desbloquear) |
| 14 | GET estado de la tasa del caso 4 | 200, `estado` `"0"`, `fechaUso` null |
| 15 | GET estado de un `TS_IDBC` de `PC_TASAHIST` | 200, `estado` `"1"`, `fechaUso` con fecha |
| 16 | GET estado de código inexistente | 404 `TASA_NO_EXISTE` |
| 17 | GET estado con letras / > 20 dígitos | 400 |
| 18 | POST body > 16 KB | 413 `SOLICITUD_MUY_GRANDE` |

**Verificación en BD después de las pruebas:**
- `PC_TASA`: el registro del caso 4 con `TS_ESTADO = '0'`.
- `FC_LOGSERVICE`: registros `InsertarTasa`, `ConsultarEstadoTasa` y `SEGURIDAD ...` con la IP en `LOGSERVICE_PARAM`.

### 12.3 Pruebas con cURL

```bash
curl -s http://localhost:5266/api/v1/health

curl -s -X POST http://localhost:5266/api/v1/tasas \
  -H "Content-Type: application/json" -H "X-API-KEY: <API_KEY>" \
  -d '{"codigoTasa":"20261031000000000002","valorTasa":0.50,"fechaDesde":"2026-10-31T00:00:00","fechaViaje":"2026-10-31T14:30:00","fechaHasta":"2026-10-31T23:59:59","idViaje":"VJ-0001","idCooperativa":"15","idSilo":"SILO-01","usuario":"olympo"}'

curl -s http://localhost:5266/api/v1/tasas/20261031000000000002/estado -H "X-API-KEY: <API_KEY>"
```

### 12.4 Prueba de carga

Objetivo de referencia: Olympo en picos de ~100 req/s.

```bash
# bombardier (https://github.com/codesenberg/bombardier)
bombardier -c 50 -d 60s -m GET -H "X-API-KEY: <API_KEY>" \
  http://192.168.5.2:8085/api/v1/tasas/20261031000000000002/estado
```

- Para POST usar JMeter o k6 con un `codigoTasa` distinto en cada petición (si no, todas dan 409).
- Por encima de 300 req/s desde una IP aparecerá `429 LIMITE_PETICIONES`: es lo esperado.
- Si el tiempo de respuesta sube por el log, evaluar la cola de log asíncrona (§21).
- **Borrar** las tasas y logs de prueba al terminar.

---

## 13. Publicación (compilar para producción)

### 13.1 Desde Visual Studio

Clic derecho al proyecto → **Publicar** → **Carpeta**:

| Opción | Valor |
|---|---|
| Configuración | Release |
| Framework | net8.0 |
| Modo de implementación | **Dependiente del marco** (requiere el runtime en el servidor) |
| Tiempo de ejecución | win-x64 |
| Carpeta destino | `Publicacion\` |

### 13.2 Por línea de comandos

```powershell
# Dependiente del marco (recomendado; requiere Hosting Bundle / runtime en el servidor)
dotnet publish -c Release -r win-x64 --self-contained false -o .\Publicacion

# Autocontenido (no requiere runtime; ~100 MB; útil para servicio de Windows sin instalar nada)
dotnet publish -c Release -r win-x64 --self-contained true -o .\Publicacion
```

> En IIS siempre se necesita el **Hosting Bundle** (por el módulo de IIS), aunque la publicación sea autocontenida.

### 13.3 Antes de copiar al servidor

- Revisar el `appsettings.json` **del paquete**: cadena cifrada **para ese servidor** (ver §17), API Key de producción, `MostrarDetalleErrores: false`, `HabilitarSwagger: false`.
- Comprimir en `Publicacion.zip` para copiarlo.
- **No sobrescribir** el `appsettings.json` del servidor en una actualización si ya está configurado (ver §16).

---

## 14. Instalación en IIS

### 14.1 Requisitos del servidor

| Elemento | Requisito |
|---|---|
| Sistema operativo | Windows Server 2016 o superior, 64 bits (probado en Windows Server 2016 Essentials) |
| IIS | 10 (rol *Web Server*) |
| Runtime | **ASP.NET Core 8.0 Hosting Bundle** (8.0.33 o posterior de la línea 8.0) |
| SQL Server | Accesible desde el servidor (puerto 1433 o el configurado) |

**No se necesita:** ASP.NET 4.x, .NET Framework, ni ninguna característica de *Application Development* de IIS. La aplicación no usa ISAPI ni CGI.

### 14.2 Características de IIS

PowerShell como administrador (lo que ya está instalado se omite):

```powershell
Install-WindowsFeature Web-Server, Web-Default-Doc, Web-Static-Content, Web-Http-Errors, Web-Http-Logging, Web-Filtering, Web-Mgmt-Console
```

### 14.3 Hosting Bundle

1. En la página de descarga de .NET 8 → sección **ASP.NET Core Runtime 8.0.x** → fila **Windows** → enlace **Hosting Bundle**.
   - Archivo correcto: `dotnet-hosting-8.0.xx-win.exe`.
   - **No** sirven: `aspnetcore-runtime-...-win-x64.exe` (no trae el módulo de IIS), el SDK ni el Desktop Runtime.
2. Ejecutarlo como administrador. Instala el ASP.NET Core Runtime, el .NET Runtime y el **ASP.NET Core Module V2**.
3. Reiniciar IIS:
   ```cmd
   net stop was /y
   net start w3svc
   ```
4. Verificar:
   - Administrador de IIS → nodo del servidor → **Módulos** → debe aparecer `AspNetCoreModuleV2`.
   - En una **CMD nueva**: `dotnet --list-runtimes` → `Microsoft.AspNetCore.App 8.0.xx`. Si no reconoce el comando, abrir otra CMD (el PATH se actualiza solo en ventanas nuevas) o usar `"C:\Program Files\dotnet\dotnet.exe" --list-runtimes`. IIS no depende del PATH.

> **Orden:** IIS primero, Hosting Bundle después. Si se agregan características de IIS más tarde, ejecutar el instalador de nuevo con **Reparar**.

### 14.4 Copiar la aplicación

Descomprimir el paquete en, por ejemplo, `C:\inetpub\wwwroot\TrafficControlTasasService` (ruta usada en el servidor de Manta).

### 14.5 Grupo de aplicaciones

Administrador de IIS → **Grupos de aplicaciones** → Agregar:

| Opción | Valor |
|---|---|
| Nombre | `TrafficControl.Tasas.Api` |
| Versión de .NET CLR | **Sin código administrado** |
| Modo de canalización | Integrada |

**Configuración avanzada:**

| Opción | Valor | Motivo |
|---|---|---|
| Modo de inicio | `AlwaysRunning` | Arranca con IIS |
| Tiempo de espera de inactividad (min) | `0` | No se "duerme"; la primera petición de Olympo no tarda |
| Reciclaje → intervalo regular (min) | `0` o un horario de madrugada | Evita reciclajes en hora pico |
| Identidad | `ApplicationPoolIdentity` | Si la cadena usa `Integrated Security`, usar una cuenta con acceso a SQL |

### 14.6 Sitio

Administrador de IIS → **Sitios** → Agregar sitio web:

| Opción | Valor |
|---|---|
| Nombre | `TrafficControl.Tasas.Api` |
| Grupo de aplicaciones | `TrafficControl.Tasas.Api` |
| Ruta física | `C:\inetpub\wwwroot\TrafficControlTasasService` |
| Enlace | `http`, IP del servidor o *Todas las no asignadas*, puerto **8085** |

Para la opción **Examinar** del sitio, opcionalmente activar *Precarga habilitada* (*Preload Enabled*) en la configuración avanzada.

### 14.7 Permisos y firewall

```powershell
# Lectura para la identidad del pool
icacls "C:\inetpub\wwwroot\TrafficControlTasasService" /grant "IIS AppPool\TrafficControl.Tasas.Api:(OI)(CI)RX"

# Puerto del sitio
New-NetFirewallRule -DisplayName "TrafficControl.Tasas.Api" -Direction Inbound -Protocol TCP -LocalPort 8085 -Action Allow
```

Restringir la lectura de la carpeta a administradores y a la identidad del pool, porque `appsettings.json` contiene la API Key en texto plano.

### 14.8 web.config

Lo genera la publicación. Valores relevantes:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet" arguments=".\TrafficControl.Tasas.Api.dll"
                  stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" hostingModel="inprocess" />
    </system.webServer>
  </location>
</configuration>
```

- `hostingModel="inprocess"`: la app corre dentro de `w3wp.exe` (mejor rendimiento).
- **Ocultar la cabecera `Server: Microsoft-IIS/10.0` (opcional):** agregar `<security><requestFiltering removeServerHeader="true" /></security>` dentro de `<system.webServer>` **solo en Windows Server 2019 o superior** (IIS 10 versión 1709+). En **Windows Server 2016** ese atributo no existe y provoca un **error 500.19**. Ahí se deja la cabecera o se quita con una regla de salida de URL Rewrite.
- **Autocontenido:** `processPath=".\TrafficControl.Tasas.Api.exe"` y sin `arguments`.

### 14.9 Verificación

| Prueba | Resultado |
|---|---|
| `http://localhost:8085/api/v1/health` (en el servidor) | `API_OK` |
| `http://192.168.5.2:8085/api/v1/health` (otra PC de la red) | `API_OK` |
| `/api/v1/health/db` con key | `BD_OK` |
| `http://localhost:8085/` | Swagger si `HabilitarSwagger = true`; si no, `401 API_KEY_REQUERIDA` (**correcto**) |

### 14.10 Logs de arranque (stdout)

Si la app no levanta (500.30, 500.31…):

1. En el `web.config`: `stdoutLogEnabled="true"`.
2. Crear la carpeta `logs` dentro del sitio, con escritura para `IIS AppPool\TrafficControl.Tasas.Api`.
3. Reintentar y revisar `logs\stdout_*.log`.
4. Volver a `false` al terminar; si no, el archivo crece sin límite.

También: **Visor de eventos → Registros de Windows → Aplicación**, origen *IIS AspNetCore Module V2*.

---

## 15. Instalación como servicio de Windows

Alternativa a IIS: Kestrel corriendo como servicio. Útil en equipos sin IIS.

### 15.1 Requisitos

- **ASP.NET Core Runtime 8.0 x64** (o el Hosting Bundle, que lo incluye).
- O publicar **autocontenido** (§13.2) y no instalar nada.
- Paquete `Microsoft.Extensions.Hosting.WindowsServices` (ya incluido en el proyecto).

### 15.2 Instalar

1. Publicar y copiar a, por ejemplo, `C:\Servicios\TrafficControl.Tasas.Api`.
2. Revisar `ServicioWindows:Url` en `appsettings.json` (por defecto `http://0.0.0.0:5080`).
3. CMD como administrador:

```cmd
sc.exe create "TrafficControl.Tasas.Api" binPath= "C:\Servicios\TrafficControl.Tasas.Api\TrafficControl.Tasas.Api.exe" start= delayed-auto DisplayName= "Traffic Control - API de Tasas"
sc.exe description "TrafficControl.Tasas.Api" "API REST de recepcion y consulta de tasas (Only Control S.A.)"

:: Reinicio automático si el servicio se cae (3 intentos, 1 minuto entre cada uno)
sc.exe failure "TrafficControl.Tasas.Api" reset= 86400 actions= restart/60000/restart/60000/restart/60000

sc.exe start "TrafficControl.Tasas.Api"
```

> En `sc.exe` el espacio **después** de `=` es obligatorio (`binPath= "..."`).

4. Firewall:
   ```powershell
   New-NetFirewallRule -DisplayName "TrafficControl.Tasas.Api (Servicio)" -Direction Inbound -Protocol TCP -LocalPort 5080 -Action Allow
   ```
5. Probar `http://localhost:5080/api/v1/health`.

### 15.3 Cuenta del servicio

- Por defecto corre como **LocalSystem**.
- Si la cadena usa `Integrated Security=True`, configurar una cuenta de dominio o de servicio con acceso a SQL: `services.msc` → Propiedades → Iniciar sesión.

### 15.4 Logs

- Al arrancar se registra `TrafficControl.Tasas.Api iniciado. Modo: Servicio de Windows. Ruta: ...`.
- Los logs van al **Visor de eventos → Aplicación** (proveedor EventLog que agrega `UseWindowsService`).

### 15.5 Detener, actualizar y desinstalar

```cmd
sc.exe stop   "TrafficControl.Tasas.Api"
:: copiar los nuevos archivos (sin pisar appsettings.json)
sc.exe start  "TrafficControl.Tasas.Api"

:: Desinstalar
sc.exe stop   "TrafficControl.Tasas.Api"
sc.exe delete "TrafficControl.Tasas.Api"
```

> **No ejecutar IIS y el servicio a la vez con el mismo puerto.** Por defecto usan puertos distintos (8085 / 5080).

---

## 16. Actualizar una versión instalada

### En IIS

1. Respaldar la carpeta del sitio, **sobre todo `appsettings.json`**.
2. Crear un archivo `app_offline.htm` en la raíz del sitio. El módulo de IIS detiene la app y libera las DLL; las peticiones reciben ese HTML con un 503.
3. Copiar los archivos nuevos **excepto `appsettings.json`**. Si la versión trae parámetros nuevos, agregarlos a mano al del servidor.
4. Borrar `app_offline.htm`.
5. Probar `/api/v1/health` y `/api/v1/health/db`.

Sin `app_offline.htm`, Windows no deja sobrescribir `TrafficControl.Tasas.Api.dll` porque está en uso. La alternativa es detener el grupo de aplicaciones.

### Como servicio

Ver §15.5.

---

## 17. Cifrado de la cadena de conexión

- Herramienta: `CriptografiaModerna.exe` (la misma que usa `ParkingControl.Facturacion.Worker`).
- La clase `clsCriptografiaModerna` está en la carpeta `Criptografia`. Si se copió del Worker, su namespace puede seguir siendo `ParkingControl.Facturacion.Worker.Criptografia`; se recomienda renombrarlo a `TrafficControl.Tasas.Api.Criptografia`.
- **Verificar si el cifrado depende del equipo.** Si la clase usa DPAPI o una clave de máquina o de usuario, lo cifrado en la PC de desarrollo **no se descifra en el servidor**. En ese caso, cifrar la cadena **en el servidor** con el `.exe`.
- Si `Desencriptar` devuelve vacío en vez de lanzar una excepción, la API arranca pero falla al conectar a la BD. Comprobarlo con `/api/v1/health/db`.

---

## 18. Gestión de API Keys

### Generar una key

PowerShell 7:

```powershell
[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

Resultado: 44 caracteres, por ejemplo `EaoRVqCBJzkYpO2XbKAAQatlSi6Hlan2boWDZcAghYU=`. **No usar claves adivinables** (`Olympo2026`, nombre del cliente + año).

### Entregar

Por un canal seguro y separado de la documentación. Nunca por el mismo correo que el documento técnico.

### Rotar sin cortar el servicio

1. Agregar la nueva key a `ApiKeys` (quedan dos vigentes) → reciclar el pool o reiniciar el servicio.
2. El cliente cambia a la nueva key.
3. Confirmar en `FC_LOGSERVICE` que ya llegan peticiones exitosas con la nueva.
4. Quitar la key anterior → reciclar o reiniciar.

> `appsettings.json` se lee al arrancar: cualquier cambio requiere reciclar el grupo de aplicaciones o reiniciar el servicio.

---

## 19. Solución de problemas

| Síntoma | Causa | Solución |
|---|---|---|
| **HTTP 500.19**, código `0x8007000d` | IIS no reconoce `<aspNetCore>`: falta el ASP.NET Core Module | Instalar el **Hosting Bundle** (no el runtime suelto) y reiniciar IIS (§14.3) |
| **HTTP 500.30** – falla al iniciar | Excepción en `Program.cs`: no descifra la cadena, `ApiKeys` vacía, IP mal escrita en `IpsPermitidas`, `appsettings.json` mal formado | Activar stdout (§14.10) y leer el error |
| **HTTP 500.31** – no encuentra el runtime | Falta el runtime 8.0 o la versión es menor que la compilada | Instalar o actualizar el Hosting Bundle 8.0 |
| **502.5** / proceso falla | Publicación incompleta o arquitectura incorrecta | Republicar `win-x64` y verificar la carpeta |
| `401 API_KEY_REQUERIDA` al abrir `http://servidor:8085/` | La raíz no es pública y Swagger está apagado | Correcto. Usar `/api/v1/health`, o `HabilitarSwagger = true` en pruebas |
| `401 API_KEY_INVALIDA` con la key correcta | Se envía otro valor (ej. uno cifrado), hay espacios o Swagger guardó una key vieja | Enviar exactamente el valor de `ApiKeys`. En Swagger: **Logout** → **Authorize** |
| `429 IP_BLOQUEADA` durante pruebas | 10 keys inválidas desde tu IP | Esperar 15 min o reciclar el pool / reiniciar el servicio |
| `500` `Unable to cast object of type 'System.Int64' to type 'System.Int32'` | El modelo tiene `int` y la columna en esa BD es `bigint` (caso real: `TS_ID`) | Cambiar a `long` (y `IdTasa` a `long`) o hacer el scaffold contra esa BD (§6.3). El INSERT probablemente sí se guardó |
| `503 BD_ERROR` en `/health/db` | Cadena incorrecta o no descifrada, firewall de SQL, permisos del usuario | Probar la conexión con SSMS desde el servidor; revisar §17 y el puerto 1433 |
| El INSERT falla con un error sobre `OUTPUT` / triggers | La tabla tiene triggers | `HasTrigger(...)` en el DbContext (§6.3) |
| `dotnet` no se reconoce en CMD | CMD abierta antes de instalar | Abrir una CMD nueva; para IIS no importa |
| Swagger no aparece | `HabilitarSwagger = false`, o se navega a `/swagger` cuando está en la raíz | Revisar el flag; Swagger está en `/` |
| Las fechas dan `VALIDACION_ERROR` | Formato distinto de `yyyy-MM-ddTHH:mm:ss` (sin `T`, con milisegundos o con zona) | Ajustar el formato en el cliente |
| Todas las IPs bloqueadas o limitadas a la vez | Hay un proxy delante y todas llegan con su IP | Configurar `ForwardedHeaders` (§8.5) |
| El primer request tarda varios segundos | Pool inactivo o recién reciclado | `AlwaysRunning` + tiempo de inactividad `0` (§14.5) |

**Comparar tipos entre bases (desarrollo y producción):**

```sql
SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME IN ('PC_TASA', 'PC_TASAHIST', 'FC_LOGSERVICE')
ORDER BY TABLE_NAME, ORDINAL_POSITION;
```

---

## 20. Checklist de producción

**Servidor**
- [ ] Rol IIS con las características de §14.2
- [ ] Hosting Bundle 8.0 instalado y `AspNetCoreModuleV2` visible en Módulos
- [ ] Grupo de aplicaciones *Sin código administrado*, `AlwaysRunning`, inactividad `0`
- [ ] Sitio en el puerto acordado; firewall abierto solo para el origen del ERP
- [ ] Permisos de carpeta restringidos (`appsettings.json` tiene la API Key)
- [ ] HTTPS con certificado si el servicio sale de la red interna

**appsettings.json del servidor**
- [ ] `CadenaTasas` cifrada **en/para este servidor** y probada con `/health/db`
- [ ] Usuario SQL con permisos mínimos (§6.4)
- [ ] `ApiKeys` con una key aleatoria de producción (no la de pruebas)
- [ ] `MostrarDetalleErrores: false`
- [ ] `HabilitarSwagger: false` (salvo que el cliente lo pida)
- [ ] `IpsPermitidas` con la IP del ERP si la API queda expuesta
- [ ] `Logging` de EF Core en `Warning`

**Base de datos**
- [ ] Modelo alineado con los tipos de la BD de producción (`TS_ID`)
- [ ] Tasas y logs de prueba eliminados

**Pruebas finales**
- [ ] `/api/v1/health` → `API_OK` desde la red del ERP
- [ ] `/api/v1/health/db` → `BD_OK`
- [ ] Alta de una tasa real de prueba → 201 → consulta → `"0"`
- [ ] Registro visible en `FC_LOGSERVICE` con la IP del ERP
- [ ] `stdoutLogEnabled="false"` en el `web.config`

**Entrega**
- [ ] API Key entregada por canal seguro
- [ ] Documento técnico para el cliente entregado
- [ ] URL de producción comunicada

---

## 21. Pendientes y mejoras sugeridas

| Mejora | Descripción | Prioridad |
|---|---|---|
| **HTTPS** | Certificado en el binding de IIS, o en `Kestrel:Endpoints` en modo servicio. Obligatorio si se publica fuera de la red interna. | Alta (si es pública) |
| **Proyecto de pruebas** | `TrafficControl.Tasas.Api.Tests` con xUnit: pruebas unitarias de `TasaService.Validar`, `DescribirEstado` y `DateTimeIsoConverter`; pruebas de integración con `WebApplicationFactory<Program>` para los middlewares (401, 403, 429, 413) y los controllers. Requiere `public partial class Program { }` al final de `Program.cs`. | Media |
| **Log asíncrono** | Encolar el log en un `Channel<T>` y que un `BackgroundService` lo inserte en lotes. Baja la latencia, pero un reinicio brusco pierde los logs que estaban en cola. Evaluar con la prueba de carga. | Según carga |
| **JWT** | Si se acuerda con el cliente: `POST /api/v1/auth/token` con usuario y clave del sistema y token con expiración. | Baja |
| **`ForwardedHeaders`** | Solo si se instala un proxy o balanceador delante (§8.5). | Condicional |
| **Bloqueo distribuido** | El contador de intentos está en memoria; con varias instancias detrás de un balanceador, pasar a Redis o a SQL. | Condicional |
| **Datos sin columna** | `fechaViaje`, `idViaje`, `idSilo` y `usuario` solo quedan en el log. Si el negocio los necesita para reportes, evaluar con Traffic Control una tabla complementaria. | A definir |
| **Health checks nativos** | Migrar `/health` a `AddHealthChecks()` si se integra con una herramienta de monitoreo estándar. | Baja |

---

*Only Control S.A. – Technological Solutions · Documento interno.*
