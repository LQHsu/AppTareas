using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.Auth;
using TaskManager.Api.Hubs;
using TaskManager.Api.Middleware;
using TaskManager.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// appsettings.Local.json: secretos reales de este entorno (connection
// strings con password, etc.), NUNCA versionado (ver .gitignore). Prioridad
// mas alta que appsettings.json/appsettings.{Environment}.json, asi que
// solo hace falta poner ahi las llaves que en appsettings.json quedaron
// como placeholder. Opcional: si no existe, la app sigue arrancando con
// los placeholders (y falla al conectar, como es de esperar).
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// --- EF Core / SQL Server ---
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// --- Autenticacion contra Keycloak (valida el JWT emitido por el realm) ---
// "Keycloak:MockAuth" permite seguir desarrollando cuando el servidor de
// Keycloak no esta disponible: en vez de validar el JWT contra el
// Authority, acepta el token falso que genera el AuthService del
// frontend en modo mock. Ver MockAuthHandler para el detalle. NUNCA
// activar esta llave en produccion.
var keycloakAuthority = builder.Configuration["Keycloak:Authority"];
// Ejemplo: https://sso.uam.mx/realms/uamx
var mockAuthEnabled = builder.Configuration.GetValue<bool>("Keycloak:MockAuth");

if (mockAuthEnabled)
{
    builder.Services.AddAuthentication(MockAuthHandler.SchemeName)
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, MockAuthHandler>(
            MockAuthHandler.SchemeName, options => { });
}
else
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = keycloakAuthority;
            options.Audience = builder.Configuration["Keycloak:ClientId"]; // "task-manager-uamx"
            options.MapInboundClaims = false;
            // El Keycloak propio en Docker corre en HTTP en dev
            // (docker/keycloak/, "start-dev" sin TLS - ver su README).
            // Exigir HTTPS metadata ahi tumbaria el arranque al intentar
            // descargar /.well-known/openid-configuration. En produccion
            // (Fase 6) el Authority real usa HTTPS y esto vuelve a exigirse.
            options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

            // SignalR (websockets/SSE) no puede mandar el header
            // "Authorization" en el handshake de conexion: el cliente
            // (accessTokenFactory, ver realtime.service.ts) manda el
            // token como query string "access_token" en su lugar. Solo
            // se acepta asi para el propio hub, no para el resto de la
            // API (evita que un token quede expuesto en logs de acceso
            // de cualquier endpoint HTTP normal).
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(accessToken) &&
                        context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                },
            };
        });
}

builder.Services.AddAuthorization();
builder.Services.AddSignalR();

// --- CORS para el frontend Angular ---
builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularApp", policy =>
    {
        policy.WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
              .AllowAnyHeader()
              .AllowAnyMethod()
              // SignalR manda su negotiate con "credentials: include" (a
              // diferencia del HttpClient normal de Angular, que no lo
              // usa) - sin esto el navegador bloquea la respuesta con un
              // error de CORS antes de que el codigo la vea.
              .AllowCredentials();
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (mockAuthEnabled)
{
    app.Logger.LogWarning(
        "*** MOCK AUTH ACTIVO: no se esta validando contra Keycloak. Solo para desarrollo local. ***");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AngularApp");
app.UseAuthentication();
app.UseMiddleware<BanCheckMiddleware>();
app.UseAuthorization();
app.MapControllers();
app.MapHub<TaskHub>("/hubs/tasks");

app.Run();