using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.DTOs;
using TaskManager.Api.Hubs;
using TaskManager.Domain.Entities;
using TaskManager.Infrastructure;

namespace TaskManager.Api.Controllers;

[ApiController]
[Route("api/tasks/{taskId:guid}/comments")]
[Authorize]
public class TaskCommentsController : ControllerBase
{
    private const int MaxContentLength = 4000;

    private readonly AppDbContext _db;
    private readonly IHubContext<TaskHub> _hub;

    public TaskCommentsController(AppDbContext db, IHubContext<TaskHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    // Mismos grupos que TasksController.NotificarCambio: el grupo del
    // proyecto (cubre a cualquier miembro viendo el tablero, aunque no
    // sea creador/asignado de ESTA tarea puntual) + el creador y el
    // asignado (cubre "Mis tareas", que no se une a ningun grupo de
    // proyecto). Un Set evita mandar el mismo evento dos veces a la
    // misma conexion si, por ejemplo, el creador tambien es miembro.
    private Task NotificarComentario<T>(TaskItem task, T payload, string eventName)
    {
        var grupos = new HashSet<string>();

        if (task.ProjectId.HasValue)
        {
            grupos.Add(TaskHubGroups.Project(task.ProjectId.Value));
        }

        grupos.Add(TaskHubGroups.User(task.CreatedById));

        if (task.AssignedToId is not null)
        {
            grupos.Add(TaskHubGroups.User(task.AssignedToId));
        }

        return Task.WhenAll(grupos.Select(g => _hub.Clients.Group(g).SendAsync(eventName, payload)));
    }

    // GET /api/tasks/{taskId}/comments
    // Mismo criterio de autorizacion que ver la tarea: cualquier
    // miembro/dueno del proyecto, o el creador/asignado si es suelta.
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TaskCommentDto>>> GetByTask(Guid taskId)
    {
        var userId = GetUserIdFromToken();

        var task = await _db.Tasks.Include(t => t.Project).FirstOrDefaultAsync(t => t.Id == taskId);
        if (task is null) return NotFound();
        if (!await CanAccessTask(task, userId)) return Forbid();

        var comments = await _db.TaskComments
            .Include(c => c.User)
            .Where(c => c.TaskId == taskId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();

        return Ok(comments.Select(ToDto).ToList());
    }

    // POST /api/tasks/{taskId}/comments
    [HttpPost]
    public async Task<ActionResult<TaskCommentDto>> Create(Guid taskId, CreateTaskCommentDto dto)
    {
        var userId = GetUserIdFromToken();

        var task = await _db.Tasks.Include(t => t.Project).FirstOrDefaultAsync(t => t.Id == taskId);
        if (task is null) return NotFound();
        if (!await CanAccessTask(task, userId)) return Forbid();

        var content = dto.Content?.Trim() ?? string.Empty;
        if (content.Length == 0) return BadRequest("El comentario no puede estar vacio.");
        if (content.Length > MaxContentLength)
            return BadRequest($"El comentario excede el limite de {MaxContentLength} caracteres.");

        var comment = new TaskComment
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            UserId = userId,
            Content = content,
            CreatedAt = DateTime.UtcNow,
        };

        _db.TaskComments.Add(comment);
        await _db.SaveChangesAsync();

        var full = await _db.TaskComments.Include(c => c.User).FirstAsync(c => c.Id == comment.Id);
        var resultDto = ToDto(full);

        await NotificarComentario(task, resultDto, "CommentAdded");

        return CreatedAtAction(nameof(GetByTask), new { taskId }, resultDto);
    }

    // DELETE /api/tasks/{taskId}/comments/{commentId}
    // Puede borrarlo quien lo escribio, o quien administra la tarea
    // (dueno del proyecto, o el creador si es suelta) — igual criterio
    // que borrar un adjunto.
    [HttpDelete("{commentId:guid}")]
    public async Task<IActionResult> Delete(Guid taskId, Guid commentId)
    {
        var userId = GetUserIdFromToken();

        var task = await _db.Tasks.Include(t => t.Project).FirstOrDefaultAsync(t => t.Id == taskId);
        if (task is null) return NotFound();

        var comment = await _db.TaskComments.FirstOrDefaultAsync(c => c.Id == commentId && c.TaskId == taskId);
        if (comment is null) return NotFound();

        var isProjectOwner = task.ProjectId.HasValue && task.Project!.OwnerId == userId;
        var canManageTask = isProjectOwner || task.CreatedById == userId;
        if (comment.UserId != userId && !canManageTask) return Forbid();

        _db.TaskComments.Remove(comment);
        await _db.SaveChangesAsync();

        await NotificarComentario(task, new CommentDeletedDto(taskId, commentId), "CommentDeleted");

        return NoContent();
    }

    private async Task<bool> CanAccessTask(TaskItem task, string userId)
    {
        if (task.ProjectId.HasValue)
        {
            var isMember = await _db.ProjectMembers.AnyAsync(m => m.ProjectId == task.ProjectId && m.UserId == userId);
            var isOwner = task.Project!.OwnerId == userId;
            return isMember || isOwner;
        }

        return task.AssignedToId == userId || task.CreatedById == userId;
    }

    private static TaskCommentDto ToDto(TaskComment c) => new(
        c.Id,
        c.TaskId,
        c.UserId,
        c.User.FullName,
        c.Content,
        c.CreatedAt
    );

    private string GetUserIdFromToken()
    {
        var sub = User.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("Token sin claim 'sub'.");

        // Normalizado a minusculas - ver comentario en User.Id.
        return sub.ToLowerInvariant();
    }
}
