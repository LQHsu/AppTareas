using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.DTOs;
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

    public AdminController(AppDbContext db)
    {
        _db = db;
    }

    // GET /api/admin/users
    [HttpGet("users")]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetAllUsers()
    {
        if (!await IsSuperAdmin(GetUserIdFromToken())) return Forbid();

        var users = await _db.Users
            .Include(u => u.Area)
            .OrderBy(u => u.FullName)
            .Select(u => new UserDto(u.Id, u.Username, u.Email, u.FullName, u.AreaId, u.Area.Nombre, u.IsSuperAdmin, u.IsBanned))
            .ToListAsync();

        return Ok(users);
    }

    // PATCH /api/admin/users/{id}/super-admin
    // No se puede uno mismo quitar el flag si es el unico super admin
    // que queda (evita dejar el panel sin nadie que pueda entrar).
    [HttpPatch("users/{id}/super-admin")]
    public async Task<ActionResult<UserDto>> UpdateSuperAdmin(string id, UpdateSuperAdminDto dto)
    {
        id = id.ToLowerInvariant(); // ver comentario en User.Id
        var callerId = GetUserIdFromToken();
        if (!await IsSuperAdmin(callerId)) return Forbid();

        var target = await _db.Users.Include(u => u.Area).FirstOrDefaultAsync(u => u.Id == id);
        if (target is null) return NotFound();

        if (target.Id == callerId && !dto.IsSuperAdmin)
        {
            var otherAdmins = await _db.Users.CountAsync(u => u.IsSuperAdmin && u.Id != callerId);
            if (otherAdmins == 0)
                return BadRequest("No puedes quitarte a ti mismo: eres el unico super admin que queda.");
        }

        target.IsSuperAdmin = dto.IsSuperAdmin;
        await _db.SaveChangesAsync();

        return Ok(new UserDto(target.Id, target.Username, target.Email, target.FullName, target.AreaId, target.Area.Nombre, target.IsSuperAdmin, target.IsBanned));
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

        return Ok(new UserDto(target.Id, target.Username, target.Email, target.FullName, target.AreaId, target.Area.Nombre, target.IsSuperAdmin, target.IsBanned));
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
