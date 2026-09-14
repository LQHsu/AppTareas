using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.DTOs;
using TaskManager.Domain.Entities;
using TaskManager.Domain.Enums;
using TaskManager.Infrastructure;

namespace TaskManager.Api.Controllers;

// Carpetas para organizar proyectos. Son personales: cada usuario ve
// las suyas, mas las que le compartieron (esas, de solo lectura).
//
// Compartir una carpeta hace dos cosas a la vez:
//   1. Agrega a la persona como miembro de todos los proyectos de la
//      carpeta (si no, tendria tareas asignadas en proyectos que no
//      puede ni abrir: GetByProject y UpdateStatus exigen membresia).
//   2. REASIGNA todas las tareas de esos proyectos a esa persona,
//      pisando quien las tuviera. Es destructivo y no se puede
//      deshacer: el modelo no guarda historial de asignaciones.
[ApiController]
[Route("api/folders")]
[Authorize]
public class FoldersController : ControllerBase
{
    private readonly AppDbContext _db;

    public FoldersController(AppDbContext db)
    {
        _db = db;
    }

    // GET /api/folders
    // Las mias (como dueno) y las que me compartieron.
    [HttpGet]
    public async Task<ActionResult<IEnumerable<FolderDto>>> GetMine()
    {
        var userId = GetUserIdFromToken();

        var folders = await _db.ProjectFolders
            .Include(f => f.Owner)
            .Include(f => f.SharedWith)
            .Where(f => f.OwnerId == userId || f.SharedWithId == userId)
            .OrderBy(f => f.Name)
            .Select(f => new FolderDto(
                f.Id,
                f.Name,
                f.OwnerId,
                f.Owner.FullName,
                f.SharedWithId,
                f.SharedWith != null ? f.SharedWith.FullName : null,
                f.SharedAt,
                f.CreatedAt,
                f.Projects.Count,
                f.Projects.SelectMany(p => p.Tasks).Count(),
                f.OwnerId == userId
            ))
            .ToListAsync();

        return Ok(folders);
    }

    // POST /api/folders
    [HttpPost]
    public async Task<ActionResult<FolderDto>> Create(CreateFolderDto dto)
    {
        var userId = GetUserIdFromToken();

        var user = await _db.Users.FindAsync(userId);
        if (user is null) return BadRequest("Tu usuario no esta registrado todavia.");

        var name = dto.Name?.Trim() ?? string.Empty;
        if (name.Length == 0) return BadRequest("El nombre de la carpeta no puede estar vacio.");

        var folder = new ProjectFolder
        {
            Id = Guid.NewGuid(),
            Name = name,
            OwnerId = userId,
            CreatedAt = DateTime.UtcNow,
        };

        _db.ProjectFolders.Add(folder);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetMine), ToDto(folder, user.FullName, null, 0, 0, true));
    }

    // PATCH /api/folders/{id}
    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<FolderDto>> Update(Guid id, UpdateFolderDto dto)
    {
        var userId = GetUserIdFromToken();

        var folder = await _db.ProjectFolders.FirstOrDefaultAsync(f => f.Id == id);
        if (folder is null) return NotFound();
        if (folder.OwnerId != userId) return Forbid();

        var name = dto.Name?.Trim() ?? string.Empty;
        if (name.Length == 0) return BadRequest("El nombre de la carpeta no puede estar vacio.");

        folder.Name = name;
        await _db.SaveChangesAsync();

        return Ok(await BuildDto(folder, userId));
    }

    // DELETE /api/folders/{id}
    // Los proyectos NO se borran: quedan sin carpeta (FK con SetNull).
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = GetUserIdFromToken();

        var folder = await _db.ProjectFolders.FirstOrDefaultAsync(f => f.Id == id);
        if (folder is null) return NotFound();
        if (folder.OwnerId != userId) return Forbid();

        _db.ProjectFolders.Remove(folder);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    // PUT /api/folders/{id}/share
    // UserId con valor  -> comparte y reasigna todas las tareas.
    // UserId null       -> deja de compartir; NO revierte las tareas
    //                      (quedan con quien las tenga; no hay historial
    //                      para revertirlas).
    [HttpPut("{id:guid}/share")]
    public async Task<ActionResult<ShareFolderResultDto>> Share(Guid id, ShareFolderDto dto)
    {
        var userId = GetUserIdFromToken();

        var folder = await _db.ProjectFolders
            .Include(f => f.Projects)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (folder is null) return NotFound();
        if (folder.OwnerId != userId) return Forbid();

        // Dejar de compartir: solo se limpia la referencia.
        if (!dto.UserId.HasValue)
        {
            folder.SharedWithId = null;
            folder.SharedAt = null;
            await _db.SaveChangesAsync();

            return Ok(new ShareFolderResultDto(await BuildDto(folder, userId), 0));
        }

        if (dto.UserId.Value == userId)
            return BadRequest("No puedes compartir una carpeta contigo mismo.");

        var owner = await _db.Users.FindAsync(userId);
        var target = await _db.Users.FindAsync(dto.UserId.Value);
        if (owner is null || target is null) return BadRequest("Usuario invalido.");

        // Misma regla que invitar a un proyecto: solo gente de tu area.
        // Asi compartir no se vuelve una puerta trasera para meter gente
        // de otra area a un proyecto.
        if (target.AreaId != owner.AreaId)
            return BadRequest("Solo puedes compartir carpetas con personas de tu misma area.");

        var projectIds = folder.Projects.Select(p => p.Id).ToList();

        // 1) Acceso: agregar como miembro donde no lo sea todavia.
        if (projectIds.Count > 0)
        {
            var yaMiembroEn = await _db.ProjectMembers
                .Where(m => projectIds.Contains(m.ProjectId) && m.UserId == target.Id)
                .Select(m => m.ProjectId)
                .ToListAsync();

            foreach (var projectId in projectIds.Except(yaMiembroEn))
            {
                _db.ProjectMembers.Add(new ProjectMember
                {
                    ProjectId = projectId,
                    UserId = target.Id,
                    JoinedAt = DateTime.UtcNow,
                });
            }
        }

        // 2) Reasignacion masiva. Incluye subtareas (tambien tienen
        // ProjectId) y tareas ya terminadas o canceladas: se pidio
        // explicitamente reasignar TODAS las de la carpeta.
        var ahora = DateTime.UtcNow;
        var tasks = await _db.Tasks
            .Where(t => t.ProjectId.HasValue && projectIds.Contains(t.ProjectId.Value))
            .ToListAsync();

        var reasignadas = 0;

        foreach (var task in tasks)
        {
            if (task.AssignedToId == target.Id) continue;

            task.AssignedToId = target.Id;
            task.FechaAsignacion = ahora;
            task.UpdatedAt = ahora;
            reasignadas++;

            // Mismo comportamiento que PATCH /tasks/{id}/assign: una
            // tarea que seguia en Creada pasa a Asignada, con bitacora.
            if (task.Status == TaskItemStatus.Creada)
            {
                var oldStatus = task.Status;
                task.Status = TaskItemStatus.Asignada;

                _db.TaskStatusHistories.Add(new TaskStatusHistory
                {
                    Id = Guid.NewGuid(),
                    TaskId = task.Id,
                    ChangedById = userId,
                    OldStatus = oldStatus,
                    NewStatus = task.Status,
                    ChangedAt = ahora,
                });
            }
        }

        folder.SharedWithId = target.Id;
        folder.SharedAt = ahora;

        await _db.SaveChangesAsync();

        return Ok(new ShareFolderResultDto(await BuildDto(folder, userId), reasignadas));
    }

    private async Task<FolderDto> BuildDto(ProjectFolder folder, Guid viewerId)
    {
        var owner = await _db.Users.FindAsync(folder.OwnerId);
        var sharedWith = folder.SharedWithId.HasValue
            ? await _db.Users.FindAsync(folder.SharedWithId.Value)
            : null;

        var projectCount = await _db.Projects.CountAsync(p => p.FolderId == folder.Id);
        var taskCount = await _db.Tasks.CountAsync(t =>
            t.ProjectId.HasValue && t.Project!.FolderId == folder.Id);

        return ToDto(folder, owner?.FullName ?? "", sharedWith?.FullName, projectCount, taskCount,
            folder.OwnerId == viewerId);
    }

    private static FolderDto ToDto(
        ProjectFolder f,
        string ownerName,
        string? sharedWithName,
        int projectCount,
        int taskCount,
        bool isOwner) =>
        new(f.Id, f.Name, f.OwnerId, ownerName, f.SharedWithId, sharedWithName, f.SharedAt,
            f.CreatedAt, projectCount, taskCount, isOwner);

    private Guid GetUserIdFromToken()
    {
        var sub = User.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("Token sin claim 'sub'.");

        return Guid.Parse(sub);
    }
}
