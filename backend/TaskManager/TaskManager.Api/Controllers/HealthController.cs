using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskManager.Infrastructure;

namespace TaskManager.Api.Controllers;

// Health check para el script de despliegue: valida que la API arranco y
// que llega a la BD antes de dar por bueno un release, y dispara rollback
// si no. Apache solo hace proxy de /api y /hubs, asi que /health solo es
// alcanzable desde localhost, no desde internet.
//
// Es un controller y no un MapGet con lambda a proposito: Obfuscar borra
// los nombres de parametro de las lambdas y Minimal API los exige.
[ApiController]
[AllowAnonymous]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly AppDbContext _db;

    public HealthController(AppDbContext db)
    {
        _db = db;
    }

    // GET /health
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        return await _db.Database.CanConnectAsync(ct)
            ? Ok(new { status = "ok" })
            : StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}
