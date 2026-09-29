using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.DTOs;
using TaskManager.Api.Notificaciones;
using TaskManager.Domain.Enums;
using TaskManager.Infrastructure;

namespace TaskManager.Api.Controllers;

// Panel de super admin: gestion global de usuarios y vista global de
// proyectos, sin importar area/membresia. Cada accion valida
// explicitamente que el que llama sea super admin (misma convencion
// que el resto del proyecto: nada de [Authorize(Roles=...)], porque no
// hay sincronizacion de roles de Keycloak — ver comentario en User.cs).
[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly EmailService _email;
    private readonly GoogleChatClient _chat;

    public AdminController(AppDbContext db, EmailService email, GoogleChatClient chat)
    {
        _db = db;
        _email = email;
        _chat = chat;
    }

    // GET /api/admin/users
    [HttpGet("users")]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetAllUsers()
    {
        if (!await IsSuperAdmin(GetUserIdFromToken())) return Forbid();

        var users = await _db.Users
            .Include(u => u.Area)
            .OrderBy(u => u.FullName)
            .Select(u => new UserDto(u.Id, u.Username, u.Email, u.FullName, u.AreaId, u.Area.Nombre, u.IsSuperAdmin, u.IsBanned, u.IsCoordinador, u.NotifyByEmail, u.NotifyByChat))
            .ToListAsync();

        return Ok(users);
    }

    // PATCH /api/admin/users/{id}/super-admin
    // Nadie se puede quitar el flag a si mismo, ni aunque queden otros
    // super admins: un auto-degradado por error de UI (toggle
    // equivocado) es dificil de deshacer sin pedirle el favor a otro
    // admin o entrar por SQL. Que otro super admin te lo quite a ti si
    // hace falta.
    [HttpPatch("users/{id}/super-admin")]
    public async Task<ActionResult<UserDto>> UpdateSuperAdmin(string id, UpdateSuperAdminDto dto)
    {
        id = id.ToLowerInvariant(); // ver comentario en User.Id
        var callerId = GetUserIdFromToken();
        if (!await IsSuperAdmin(callerId)) return Forbid();

        if (id == callerId && !dto.IsSuperAdmin)
            return BadRequest("No puedes quitarte a ti mismo el rol de super admin. Pidele a otro super admin que lo haga.");

        var target = await _db.Users.Include(u => u.Area).FirstOrDefaultAsync(u => u.Id == id);
        if (target is null) return NotFound();

        target.IsSuperAdmin = dto.IsSuperAdmin;
        await _db.SaveChangesAsync();

        return Ok(new UserDto(target.Id, target.Username, target.Email, target.FullName, target.AreaId, target.Area.Nombre, target.IsSuperAdmin, target.IsBanned, target.IsCoordinador, target.NotifyByEmail, target.NotifyByChat));
    }

    // PATCH /api/admin/users/{id}/coordinador
    // Otorga o quita el rol de coordinador (dar de alta numeros
    // economicos de su propia area - ver CoordinadorController). A
    // diferencia de super-admin, no hace falta bloquear el
    // auto-quitarselo: no deja a nadie sin acceso al panel, solo pierde
    // la capacidad de dar altas.
    [HttpPatch("users/{id}/coordinador")]
    public async Task<ActionResult<UserDto>> UpdateCoordinador(string id, UpdateCoordinadorDto dto)
    {
        id = id.ToLowerInvariant(); // ver comentario en User.Id
        if (!await IsSuperAdmin(GetUserIdFromToken())) return Forbid();

        var target = await _db.Users.Include(u => u.Area).FirstOrDefaultAsync(u => u.Id == id);
        if (target is null) return NotFound();

        target.IsCoordinador = dto.IsCoordinador;
        await _db.SaveChangesAsync();

        return Ok(new UserDto(target.Id, target.Username, target.Email, target.FullName, target.AreaId, target.Area.Nombre, target.IsSuperAdmin, target.IsBanned, target.IsCoordinador, target.NotifyByEmail, target.NotifyByChat));
    }

    // PATCH /api/admin/users/{id}/ban
    // Bloquea (o desbloquea) el acceso de un usuario a toda la app.
    // Keycloak sigue autenticandolo igual (no hay forma de revocar eso
    // desde aca); lo que corta las peticiones es BanCheckMiddleware, que
    // revisa este flag en cada request autenticado. No te puedes banear
    // a ti mismo (te dejaria sin forma de deshacerlo salvo por SQL).
    [HttpPatch("users/{id}/ban")]
    public async Task<ActionResult<UserDto>> UpdateBanned(string id, UpdateBannedDto dto)
    {
        id = id.ToLowerInvariant(); // ver comentario en User.Id
        var callerId = GetUserIdFromToken();
        if (!await IsSuperAdmin(callerId)) return Forbid();

        if (id == callerId) return BadRequest("No puedes bloquearte a ti mismo.");

        var target = await _db.Users.Include(u => u.Area).FirstOrDefaultAsync(u => u.Id == id);
        if (target is null) return NotFound();

        target.IsBanned = dto.IsBanned;
        await _db.SaveChangesAsync();

        return Ok(new UserDto(target.Id, target.Username, target.Email, target.FullName, target.AreaId, target.Area.Nombre, target.IsSuperAdmin, target.IsBanned, target.IsCoordinador, target.NotifyByEmail, target.NotifyByChat));
    }

    // GET /api/admin/projects
    // A diferencia de ProjectsController.GetMine, aqui se listan TODOS
    // los proyectos (incluidos archivados), sin filtrar por membresia.
    [HttpGet("projects")]
    public async Task<ActionResult<IEnumerable<AdminProjectDto>>> GetAllProjects()
    {
        if (!await IsSuperAdmin(GetUserIdFromToken())) return Forbid();

        var projects = await _db.Projects
            .Include(p => p.Area)
            .Include(p => p.Owner)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new AdminProjectDto(
                p.Id,
                p.Name,
                p.Description,
                p.AreaId,
                p.Area.Nombre,
                p.OwnerId,
                p.Owner.FullName,
                p.IsArchived,
                p.CreatedAt,
                p.Members.Count,
                p.Tasks.Count(t => t.Status != TaskItemStatus.Cancelada),
                p.Tasks.Count(t => t.Status == TaskItemStatus.Terminada)
            ))
            .ToListAsync();

        return Ok(projects);
    }

    // POST /api/admin/test-email
    // Solo para confirmar que el relay SMTP institucional (Email:Host en
    // appsettings) funciona de verdad, sin tener que esperar a que algun
    // evento real de la app dispare un correo. No hace nada de negocio.
    [HttpPost("test-email")]
    public async Task<IActionResult> TestEmail(TestEmailDto dto)
    {
        if (!await IsSuperAdmin(GetUserIdFromToken())) return Forbid();

        if (!_email.Enabled)
            return BadRequest("El correo esta deshabilitado (falta Email:Host en la configuracion).");

        await _email.SendAsync(
            dto.To,
            dto.To,
            "Correo de prueba - AppTareas",
            "<p>Este es un correo de prueba de AppTareas. Si lo recibiste, la configuración del relay SMTP institucional funciona.</p>"
        );

        return NoContent();
    }

    // POST /api/admin/test-chat-message
    // Solo para probar el flujo completo de Google Chat (encontrar el DM,
    // mandar la tarjeta, clic en "Marcar como Atendida", dialogo,
    // confirmacion - ver plan) contra una tarea real, sin depender de que
    // un evento real de la app lo dispare todavia. "to" es el email de
    // AppTareas de alguien que YA vinculo su cuenta con el bot (ver
    // User.GoogleChatUserId / ChatInteractionsController) - no basta con
    // haberle escrito, App Authentication busca por el id de Directory,
    // no por email (ver GoogleChatClient.FindDmAsync).
    [HttpPost("test-chat-message")]
    public async Task<IActionResult> TestChatMessage(TestChatMessageDto dto)
    {
        var callerId = GetUserIdFromToken();
        if (!await IsSuperAdmin(callerId)) return Forbid();

        if (!_chat.Enabled)
            return BadRequest("Google Chat esta deshabilitado (falta GoogleChat:ServiceAccountJsonBase64 en la configuracion).");

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == dto.TaskId);
        if (task is null) return NotFound("Esa tarea no existe.");

        var target = await _db.Users.FirstOrDefaultAsync(u => u.Email == dto.To);
        if (target is null) return NotFound($"No hay ningun usuario de AppTareas con el correo {dto.To}.");
        if (string.IsNullOrEmpty(target.GoogleChatUserId))
            return BadRequest($"{dto.To} todavia no vinculo su cuenta con el bot de Chat (tiene que escribirle y confirmar su correo primero).");

        string spaceName;
        try
        {
            spaceName = await _chat.FindDmAsync(target.GoogleChatUserId);
        }
        catch (Exception ex)
        {
            return BadRequest($"No se pudo abrir el DM con {dto.To}: {ex.Message}");
        }

        await _chat.SendTaskCardAsync(
            spaceName,
            task.Id,
            task.Title,
            "Mensaje de prueba de AppTareas - esto no representa un cambio real todavía.",
            incluirBotonAtendida: true,
            assignedUserId: callerId
        );

        return NoContent();
    }

    private async Task<bool> IsSuperAdmin(string userId)
    {
        return await _db.Users.AnyAsync(u => u.Id == userId && u.IsSuperAdmin);
    }

    private string GetUserIdFromToken()
    {
        var sub = User.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("Token sin claim 'sub'.");

        // Normalizado a minusculas - ver comentario en User.Id.
        return sub.ToLowerInvariant();
    }
}
