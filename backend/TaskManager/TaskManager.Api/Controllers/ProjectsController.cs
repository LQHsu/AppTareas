using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.DTOs;
using TaskManager.Domain.Entities;
using TaskManager.Domain.Enums;
using TaskManager.Infrastructure;

namespace TaskManager.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProjectsController(AppDbContext db)
    {
        _db = db;
    }

    // GET /api/projects
    // Regresa los proyectos donde el usuario es dueno O miembro.
    // No filtra por "solo los mios que cree" porque el requerimiento
    // original es: cualquier miembro ve todo el proyecto.
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProjectDto>>> GetMine()
    {
        var userId = GetUserIdFromToken();

        var projects = await _db.Projects
            .Include(p => p.Area)
            .Include(p => p.Owner)
            .Include(p => p.Members)
            .Where(p => p.OwnerId == userId || p.Members.Any(m => m.UserId == userId))
            .Where(p => !p.IsArchived)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ProjectDto(
                p.Id,
                p.Name,
                p.Description,
                p.AreaId,
                p.Area.Nombre,
                p.OwnerId,
                p.Owner.FullName,
                p.IsArchived,
                p.CreatedAt,
                p.Tasks.Count(t => t.Status != TaskItemStatus.Cancelada),
                p.Tasks.Count(t => t.Status == TaskItemStatus.Terminada),
                p.FolderId,
                p.Folder != null ? p.Folder.Name : null
            ))
            .ToListAsync();

        return Ok(projects);
    }

    // GET /api/projects/{id}
    // Detalle de un proyecto especifico. Necesario para que el frontend
    // sepa el AreaId (para filtrar candidatos a invitar) y el OwnerId
    // (para decidir si mostrar controles de administracion del proyecto).
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProjectDto>> GetById(Guid id)
    {
        var userId = GetUserIdFromToken();

        var project = await _db.Projects
            .Include(p => p.Area)
            .Include(p => p.Owner)
            .Include(p => p.Members)
            .Include(p => p.Tasks)
            .Include(p => p.Folder)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (project is null) return NotFound();

        var isMember = project.Members.Any(m => m.UserId == userId);
        if (!isMember && project.OwnerId != userId) return Forbid();

        return Ok(ToDto(project));
    }

    // PATCH /api/projects/{id}
    // Edita nombre y descripcion. Solo el dueno, mismo criterio que
    // invitar/quitar miembros (el resto del proyecto no se toca aca:
    // area y dueno siguen sin ser editables).
    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ProjectDto>> Update(Guid id, UpdateProjectDto dto)
    {
        var userId = GetUserIdFromToken();

        var project = await _db.Projects
            .Include(p => p.Area)
            .Include(p => p.Owner)
            .Include(p => p.Tasks)
            .Include(p => p.Folder)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (project is null) return NotFound();
        if (project.OwnerId != userId) return Forbid();

        var name = dto.Name?.Trim() ?? string.Empty;
        if (name.Length == 0) return BadRequest("El nombre del proyecto no puede estar vacio.");

        project.Name = name;
        project.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description;

        await _db.SaveChangesAsync();

        return Ok(ToDto(project));
    }

    // PATCH /api/projects/{id}/folder
    // Mueve el proyecto a una carpeta (o lo saca, con FolderId null).
    // Solo el dueno del proyecto, y solo a carpetas propias: las
    // carpetas son personales, y compartir una agrega miembros a sus
    // proyectos — permitir meter proyectos ajenos seria una forma de
    // invitar gente a un proyecto que no es tuyo.
    [HttpPatch("{id:guid}/folder")]
    public async Task<ActionResult<ProjectDto>> MoveToFolder(Guid id, MoveProjectToFolderDto dto)
    {
        var userId = GetUserIdFromToken();

        var project = await _db.Projects
            .Include(p => p.Area)
            .Include(p => p.Owner)
            .Include(p => p.Tasks)
            .Include(p => p.Folder)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (project is null) return NotFound();
        if (project.OwnerId != userId) return Forbid();

        if (dto.FolderId.HasValue)
        {
            var folder = await _db.ProjectFolders.FirstOrDefaultAsync(f => f.Id == dto.FolderId.Value);
            if (folder is null) return BadRequest("Carpeta invalida.");
            if (folder.OwnerId != userId) return Forbid();

            project.FolderId = folder.Id;
            project.Folder = folder;
        }
        else
        {
            project.FolderId = null;
            project.Folder = null;
        }

        await _db.SaveChangesAsync();

        return Ok(ToDto(project));
    }

    private static ProjectDto ToDto(Project p) => new(
        p.Id, p.Name, p.Description,
        p.AreaId, p.Area.Nombre, p.OwnerId, p.Owner.FullName,
        p.IsArchived, p.CreatedAt,
        p.Tasks.Count(t => t.Status != TaskItemStatus.Cancelada),
        p.Tasks.Count(t => t.Status == TaskItemStatus.Terminada),
        p.FolderId, p.Folder?.Name
    );

    // GET /api/projects/{id}/attachments
    // Todos los adjuntos de todas las tareas del proyecto en una sola
    // lista (incluye los de subtareas: tambien tienen ProjectId). Mismo
    // criterio de autorizacion que ver el proyecto.
    [HttpGet("{id:guid}/attachments")]
    public async Task<ActionResult<IEnumerable<ProjectAttachmentDto>>> GetAttachments(Guid id)
    {
        var userId = GetUserIdFromToken();

        var project = await _db.Projects
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (project is null) return NotFound();

        var isMember = project.Members.Any(m => m.UserId == userId);
        if (!isMember && project.OwnerId != userId) return Forbid();

        var attachments = await _db.TaskAttachments
            .Where(a => a.Task.ProjectId == id)
            .OrderByDescending(a => a.UploadedAt)
            .Select(a => new ProjectAttachmentDto(
                a.Id,
                a.TaskId,
                a.Task.Title,
                a.FileName,
                a.ContentType,
                a.FileSizeBytes,
                a.UploadedById,
                a.UploadedBy.FullName,
                a.UploadedAt
            ))
            .ToListAsync();

        return Ok(attachments);
    }

    // POST /api/projects
    // El area del proyecto se hereda del area del usuario que lo crea
    // (no se pide en el formulario, ya se definio asi desde el modelo).
    // El creador queda como Owner y ademas se agrega como ProjectMember
    // para que las queries de "proyectos donde soy miembro" lo incluyan
    // de forma consistente.
    [HttpPost]
    public async Task<ActionResult<ProjectDto>> Create(CreateProjectDto dto)
    {
        var userId = GetUserIdFromToken();

        var user = await _db.Users.FindAsync(userId);
        if (user is null) return BadRequest("Tu usuario no esta registrado todavia.");

        // Igual que en MoveToFolder: solo se puede meter a una carpeta
        // propia. Si la carpeta ya esta compartida, el proyecto nuevo
        // nace sin dueño-tarea-asignada especial: sus tareas se crean
        // como siempre y solo se reasignan la proxima vez que la
        // carpeta se comparta de nuevo (compartir no se dispara aqui).
        ProjectFolder? folder = null;
        if (dto.FolderId.HasValue)
        {
            folder = await _db.ProjectFolders.FirstOrDefaultAsync(f => f.Id == dto.FolderId.Value);
            if (folder is null) return BadRequest("Carpeta invalida.");
            if (folder.OwnerId != userId) return Forbid();
        }

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Description = dto.Description,
            AreaId = user.AreaId,
            OwnerId = user.Id,
            IsArchived = false,
            CreatedAt = DateTime.UtcNow,
            FolderId = folder?.Id,
        };

        _db.Projects.Add(project);

        _db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = user.Id,
            JoinedAt = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync();

        var area = await _db.Areas.FindAsync(user.AreaId);

        return CreatedAtAction(nameof(GetMine), new ProjectDto(
            project.Id, project.Name, project.Description,
            project.AreaId, area!.Nombre, project.OwnerId, user.FullName,
            project.IsArchived, project.CreatedAt, 0, 0,
            folder?.Id, folder?.Name
        ));
    }

    private Guid GetUserIdFromToken()
    {
        var sub = User.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("Token sin claim 'sub'.");

        return Guid.Parse(sub);
    }
}