using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.DTOs;
using TaskManager.Domain.Entities;
using TaskManager.Infrastructure;

namespace TaskManager.Api.Controllers;

// Almacenamiento en disco local (no blob/S3): el stack entero es on-prem
// (Windows + Apache + Kestrel), no hay ningun servicio de blob storage
// en el resto del proyecto, asi que agregar uno solo para esto seria
// infraestructura nueva sin necesidad real. TaskAttachment.StoragePath
// guarda solo el nombre de archivo generado (Id + extension), resuelto
// contra la carpeta configurada en "Storage:AttachmentsPath".
[ApiController]
[Route("api/tasks/{taskId:guid}/attachments")]
[Authorize]
public class TaskAttachmentsController : ControllerBase
{
    private const long MaxFileSizeBytes = 25 * 1024 * 1024; // 25 MB

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".png", ".jpg", ".jpeg", ".txt", ".zip",
    };

    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public TaskAttachmentsController(AppDbContext db, IConfiguration config, IWebHostEnvironment env)
    {
        _db = db;
        _config = config;
        _env = env;
    }

    // GET /api/tasks/{taskId}/attachments
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TaskAttachmentDto>>> GetByTask(Guid taskId)
    {
        var userId = GetUserIdFromToken();

        var task = await _db.Tasks.Include(t => t.Project).FirstOrDefaultAsync(t => t.Id == taskId);
        if (task is null) return NotFound();
        if (!await CanAccessTask(task, userId)) return Forbid();

        var attachments = await _db.TaskAttachments
            .Include(a => a.UploadedBy)
            .Where(a => a.TaskId == taskId)
            .OrderByDescending(a => a.UploadedAt)
            .ToListAsync();

        return Ok(attachments.Select(ToDto).ToList());
    }

    // POST /api/tasks/{taskId}/attachments (multipart/form-data, campo "file")
    // Mismo criterio de autorizacion que subir/editar la tarea: cualquier
    // miembro/dueno del proyecto, o el creador/asignado si es suelta.
    [HttpPost]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<ActionResult<TaskAttachmentDto>> Upload(Guid taskId, IFormFile? file)
    {
        var userId = GetUserIdFromToken();

        var task = await _db.Tasks.Include(t => t.Project).FirstOrDefaultAsync(t => t.Id == taskId);
        if (task is null) return NotFound();
        if (!await CanAccessTask(task, userId)) return Forbid();

        if (file is null || file.Length == 0) return BadRequest("Archivo vacio.");
        if (file.Length > MaxFileSizeBytes)
            return BadRequest($"El archivo excede el limite de {MaxFileSizeBytes / (1024 * 1024)} MB.");

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
            return BadRequest("Tipo de archivo no permitido.");

        var attachmentId = Guid.NewGuid();
        var storageDir = GetStorageDirectory();
        Directory.CreateDirectory(storageDir);

        // Nombre en disco generado (no el original): evita colisiones y
        // path traversal. El nombre original se conserva en FileName
        // para mostrarlo y para el nombre de descarga.
        var storedFileName = $"{attachmentId}{extension}";
        var fullPath = Path.Combine(storageDir, storedFileName);

        await using (var stream = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(stream);
        }

        var attachment = new TaskAttachment
        {
            Id = attachmentId,
            TaskId = taskId,
            // "FileName" (el que se muestra y el que se usa para
            // descargar) se renombra con el titulo de la tarea, para
            // poder ubicarlo cuando ya esta descargado mezclado con
            // otros archivos. El nombre original no se pierde: queda
            // despues del guion. StoragePath (el nombre en disco) sigue
            // siendo el Id, sin tocar.
            FileName = BuildDisplayFileName(task.Title, file.FileName, extension),
            StoragePath = storedFileName,
            ContentType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
            FileSizeBytes = file.Length,
            UploadedById = userId,
            UploadedAt = DateTime.UtcNow,
        };

        _db.TaskAttachments.Add(attachment);
        await _db.SaveChangesAsync();

        var full = await _db.TaskAttachments.Include(a => a.UploadedBy).FirstAsync(a => a.Id == attachment.Id);
        return CreatedAtAction(nameof(GetByTask), new { taskId }, ToDto(full));
    }

    // GET /api/tasks/{taskId}/attachments/{attachmentId}/download
    [HttpGet("{attachmentId:guid}/download")]
    public async Task<IActionResult> Download(Guid taskId, Guid attachmentId)
    {
        var userId = GetUserIdFromToken();

        var task = await _db.Tasks.Include(t => t.Project).FirstOrDefaultAsync(t => t.Id == taskId);
        if (task is null) return NotFound();
        if (!await CanAccessTask(task, userId)) return Forbid();

        var attachment = await _db.TaskAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId && a.TaskId == taskId);
        if (attachment is null) return NotFound();

        var fullPath = Path.Combine(GetStorageDirectory(), attachment.StoragePath);
        if (!System.IO.File.Exists(fullPath)) return NotFound("El archivo ya no esta disponible en el servidor.");

        var stream = System.IO.File.OpenRead(fullPath);
        return File(stream, attachment.ContentType, attachment.FileName);
    }

    // DELETE /api/tasks/{taskId}/attachments/{attachmentId}
    // Puede borrarlo quien lo subio, o quien administra la tarea (dueno
    // del proyecto, o el creador si es suelta).
    [HttpDelete("{attachmentId:guid}")]
    public async Task<IActionResult> Delete(Guid taskId, Guid attachmentId)
    {
        var userId = GetUserIdFromToken();

        var task = await _db.Tasks.Include(t => t.Project).FirstOrDefaultAsync(t => t.Id == taskId);
        if (task is null) return NotFound();

        var attachment = await _db.TaskAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId && a.TaskId == taskId);
        if (attachment is null) return NotFound();

        var isProjectOwner = task.ProjectId.HasValue && task.Project!.OwnerId == userId;
        var canManageTask = isProjectOwner || task.CreatedById == userId;
        if (attachment.UploadedById != userId && !canManageTask) return Forbid();

        var fullPath = Path.Combine(GetStorageDirectory(), attachment.StoragePath);
        if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);

        _db.TaskAttachments.Remove(attachment);
        await _db.SaveChangesAsync();

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

    // Config vacia -> carpeta por default dentro del content root. En
    // produccion conviene apuntar "Storage:AttachmentsPath" a una ruta
    // fuera de la carpeta de deploy, para que un redeploy no borre los
    // adjuntos ya subidos.
    private string GetStorageDirectory()
    {
        var configuredPath = _config["Storage:AttachmentsPath"];
        return string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(_env.ContentRootPath, "App_Data", "attachments")
            : configuredPath;
    }

    // "Titulo de la tarea - nombre original.ext": conserva el nombre
    // original (por si el titulo de dos tareas coincide, o para
    // reconocer el archivo en si) pero antepone la tarea para poder
    // ubicarlo entre varias descargas. Ambas partes se limpian de
    // caracteres invalidos para nombre de archivo (el titulo de la
    // tarea es texto libre, podria traer "/" o ":").
    private static string BuildDisplayFileName(string taskTitle, string originalFileName, string extension)
    {
        var nombreBase = Path.GetFileNameWithoutExtension(originalFileName);
        var titulo = SanitizeForFileName(taskTitle);
        var nombre = SanitizeForFileName(nombreBase);
        return $"{titulo} - {nombre}{extension}";
    }

    private static string SanitizeForFileName(string value)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        var limpio = new string(value.Select(c => invalidos.Contains(c) ? '-' : c).ToArray()).Trim();
        if (limpio.Length > 60) limpio = limpio[..60].TrimEnd();
        return string.IsNullOrWhiteSpace(limpio) ? "archivo" : limpio;
    }

    private static TaskAttachmentDto ToDto(TaskAttachment a) => new(
        a.Id,
        a.TaskId,
        a.FileName,
        a.ContentType,
        a.FileSizeBytes,
        a.UploadedById,
        a.UploadedBy.FullName,
        a.UploadedAt
    );

    private string GetUserIdFromToken()
    {
        var sub = User.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("Token sin claim 'sub'.");

        // Normalizado a minusculas - ver comentario en User.Id.
        return sub.ToLowerInvariant();
    }
}
