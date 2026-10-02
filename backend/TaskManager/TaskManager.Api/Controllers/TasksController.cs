using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.DTOs;
using TaskManager.Api.Hubs;
using TaskManager.Api.Notificaciones;
using TaskManager.Domain.Entities;
using TaskManager.Domain.Enums;
using TaskManager.Infrastructure;

namespace TaskManager.Api.Controllers;

[ApiController]
[Route("api/tasks")]
[Authorize]
public class TasksController : ControllerBase
{
    private readonly AppDbContext _db;
    private const int MaxCommentLength = 4000;

    private readonly IHubContext<TaskHub> _hub;
    private readonly TaskNotificationService _notifications;

    public TasksController(AppDbContext db, IHubContext<TaskHub> hub, TaskNotificationService notifications)
    {
        _db = db;
        _hub = hub;
        _notifications = notifications;
    }

    // Transmite el estado actual de la tarea a quien deberia enterarse en
    // vivo: el grupo del proyecto (si tiene) y los grupos personales del
    // creador/asignado (cubre tareas sueltas y "Mis tareas", que mezcla
    // tareas de varios proyectos sin unirse a cada uno). Un Set evita
    // mandar el mismo evento dos veces a la misma conexion cuando, por
    // ejemplo, el creador tambien es miembro del proyecto.
    // avisarTambienA: para reasignaciones, el asignado ANTERIOR (que ya
    // no aparece en `task`/`dto`) tambien necesita el evento — si no, su
    // "Mis tareas" nunca se entera de que la tarea se le fue y se queda
    // mostrandola de mas hasta que refresque a mano.
    private Task NotificarCambio(TaskItem task, TaskItemDto dto, string? avisarTambienA = null) =>
        NotificarEvento(task, dto, "TaskChanged", avisarTambienA);

    // Generaliza el fan-out a grupos (proyecto + creador + asignado) que
    // ya usaba NotificarCambio, para que Delete/Restore puedan mandar su
    // propio nombre de evento ("TaskDeleted"/"TaskChanged") con un
    // payload distinto sin duplicar la logica de a quien avisar.
    private Task NotificarEvento(TaskItem task, object payload, string evento, string? avisarTambienA = null)
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

        if (avisarTambienA is not null)
        {
            grupos.Add(TaskHubGroups.User(avisarTambienA));
        }

        return Task.WhenAll(grupos.Select(g => _hub.Clients.Group(g).SendAsync(evento, payload)));
    }

    // Misma regla en los tres lugares que la necesitan (UpdateStatus la
    // tiene inline por su propio switch de estados; Delete/Restore la
    // reusan tal cual): quien creo la tarea, o el dueno del proyecto
    // (cuenta como creador aunque no la haya creado el mismo), puede
    // borrarla/restaurarla. Quien la tiene asignada NO puede - borrar es
    // una decision mas drastica que cualquiera de las que si le tocan
    // (ver switch en UpdateStatus).
    private static bool EsCreador(TaskItem task, string userId) =>
        task.CreatedById == userId || (task.ProjectId.HasValue && task.Project!.OwnerId == userId);

    // GET /api/tasks?projectId=xxx
    // Todas las tareas de un proyecto son visibles para cualquier
    // miembro del proyecto, no solo para quien tiene asignada cada una
    // (asi se definio desde el requerimiento original).
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TaskItemDto>>> GetByProject([FromQuery] Guid projectId)
    {
        var userId = GetUserIdFromToken();

        var isMember = await _db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == userId);
        var isOwner = await _db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == userId);

        if (!isMember && !isOwner) return Forbid();

        var allTasks = await _db.Tasks
            .Include(t => t.Project)
            .ThenInclude(p => p!.Folder)
            .Include(t => t.CreatedBy)
            .Include(t => t.AssignedTo)
            .Include(t => t.StatusHistory)
            .Where(t => t.ProjectId == projectId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        // Solo un nivel de anidamiento: las subtareas se agrupan por
        // padre y se cuelgan de su tarea top-level en el DTO, no
        // aparecen tambien como filas sueltas en la lista.
        var subtasksByParent = allTasks
            .Where(t => t.ParentTaskId.HasValue)
            .GroupBy(t => t.ParentTaskId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<TaskItem>)g.ToList());

        // Incluye los ids de las subtareas: tambien salen en el DTO (como
        // hijas de su tarea top-level) y necesitan sus propios conteos.
        var conteos = await CargarConteos(allTasks.Select(t => t.Id).ToList());

        var topLevel = allTasks
            .Where(t => t.ParentTaskId is null)
            .Select(t => ToDto(t, conteos, subtasksByParent.GetValueOrDefault(t.Id)))
            .ToList();

        return Ok(topLevel);
    }

    // GET /api/tasks/mine
    // Tareas asignadas al usuario actual O creadas por el, incluyendo
    // las "sueltas" (sin proyecto). Se muestran planas (sin agrupar por
    // padre): una subtarea asignada a mi (o que yo cree) puede aparecer
    // aqui aunque su tarea padre no me pertenezca. El DTO trae
    // CreatedById/AssignedToId para que el front distinga el rol en
    // cada fila (una tarea creada y asignada a mi misma cae en ambos).
    [HttpGet("mine")]
    public async Task<ActionResult<IEnumerable<TaskItemDto>>> GetMine()
    {
        var userId = GetUserIdFromToken();

        var tasks = await _db.Tasks
            .Include(t => t.Project)
            .ThenInclude(p => p!.Folder)
            .Include(t => t.CreatedBy)
            .Include(t => t.AssignedTo)
            .Include(t => t.StatusHistory)
            .Where(t => t.AssignedToId == userId || t.CreatedById == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        var conteos = await CargarConteos(tasks.Select(t => t.Id).ToList());

        return Ok(tasks.Select(t => ToDto(t, conteos)).ToList());
    }

    // GET /api/tasks/modified?from=...&to=...
    // Resumen de Inicio: tareas que el usuario puede ver y que se
    // modificaron (UpdatedAt) dentro de [from, to). "Puede ver" = las de
    // proyectos donde es dueno/miembro, mas las que creo o tiene
    // asignadas (incluye sueltas). Lista plana, subtareas incluidas.
    [HttpGet("modified")]
    public async Task<ActionResult<IEnumerable<TaskItemDto>>> GetModified(
        [FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        var userId = GetUserIdFromToken();
        var desde = from.ToUniversalTime();
        var hasta = to.ToUniversalTime();

        var tasks = await _db.Tasks
            .Include(t => t.Project)
            .ThenInclude(p => p!.Folder)
            .Include(t => t.CreatedBy)
            .Include(t => t.AssignedTo)
            .Include(t => t.StatusHistory)
            .Where(t => t.UpdatedAt >= desde && t.UpdatedAt < hasta)
            .Where(t => t.AssignedToId == userId
                || t.CreatedById == userId
                || (t.Project != null
                    && (t.Project.OwnerId == userId || t.Project.Members.Any(m => m.UserId == userId))))
            .OrderByDescending(t => t.UpdatedAt)
            .ToListAsync();

        var conteos = await CargarConteos(tasks.Select(t => t.Id).ToList());

        return Ok(tasks.Select(t => ToDto(t, conteos)).ToList());
    }

    // GET /api/tasks/scheduled-notifications?from=...&to=...
    // Avisos de asignacion programados por el usuario (de tareas que el
    // creo), pendientes y ya enviados; los cancelados no se listan. from/to
    // opcionales: filtran por la hora programada (SendAt). Maximo 200,
    // los mas recientes primero.
    [HttpGet("scheduled-notifications")]
    public async Task<ActionResult<IEnumerable<ScheduledNotificationDto>>> GetScheduledNotifications(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var userId = GetUserIdFromToken();

        var query = _db.ScheduledTaskNotifications
            .Where(n => n.CancelledAt == null && n.Task.CreatedById == userId);

        if (from.HasValue) { var d = from.Value.ToUniversalTime(); query = query.Where(n => n.SendAt >= d); }
        if (to.HasValue) { var h = to.Value.ToUniversalTime(); query = query.Where(n => n.SendAt < h); }

        var avisos = await query
            .OrderByDescending(n => n.SendAt)
            .Take(200)
            .Select(n => new
            {
                n.Id,
                n.TaskId,
                n.Task.Title,
                n.Task.ProjectId,
                ProjectName = n.Task.Project != null ? n.Task.Project.Name : null,
                n.RecipientId,
                n.SendAt,
                n.SentAt,
            })
            .ToListAsync();

        var ids = avisos.Select(a => a.RecipientId).Distinct().ToList();
        var nombres = await _db.Users
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        return Ok(avisos.Select(a => new ScheduledNotificationDto(
            a.Id, a.TaskId, a.Title, a.ProjectId, a.ProjectName,
            nombres.GetValueOrDefault(a.RecipientId, "—"), a.SendAt, a.SentAt)));
    }

    // POST /api/tasks
    // Si ProjectId viene null y ParentTaskId tambien viene null, es una
    // tarea suelta: se valida que el asignado (si hay) pertenezca a la
    // misma area del creador. Si ParentTaskId viene, es una subtarea:
    // hereda proyecto y area de su padre (el asignado es independiente),
    // y solo se permite un nivel de anidamiento.
    [HttpPost]
    public async Task<ActionResult<TaskItemDto>> Create(CreateTaskDto dto)
    {
        var userId = GetUserIdFromToken();
        var creator = await _db.Users.FindAsync(userId);
        if (creator is null) return BadRequest("Usuario no registrado.");

        // ver comentario en User.Id
        var assignedToId = dto.AssignedToId?.ToLowerInvariant();

        Guid? projectId = dto.ProjectId;
        int areaId;

        if (dto.ParentTaskId.HasValue)
        {
            var parent = await _db.Tasks
                .Include(t => t.Project)
            .ThenInclude(p => p!.Folder)
                .FirstOrDefaultAsync(t => t.Id == dto.ParentTaskId.Value);

            if (parent is null) return BadRequest("Tarea padre invalida.");
            if (parent.ParentTaskId.HasValue)
                return BadRequest("No se pueden anidar subtareas de subtareas (solo se permite un nivel).");
            if (parent.Status == TaskItemStatus.Pausada)
                return Conflict("La tarea padre esta pausada. Reanudala antes de agregarle subtareas.");

            // Mismo criterio de autorizacion que actualizar la tarea padre.
            if (parent.ProjectId.HasValue)
            {
                var isMember = await _db.ProjectMembers.AnyAsync(m => m.ProjectId == parent.ProjectId && m.UserId == userId);
                var isOwner = parent.Project!.OwnerId == userId;
                if (!isMember && !isOwner) return Forbid();
            }
            else if (parent.AssignedToId != userId && parent.CreatedById != userId)
            {
                return Forbid();
            }

            projectId = parent.ProjectId;
            areaId = parent.AreaId;
        }
        else if (dto.ProjectId.HasValue)
        {
            var project = await _db.Projects
                .Include(p => p.Folder)
                .FirstOrDefaultAsync(p => p.Id == dto.ProjectId.Value);
            if (project is null) return BadRequest("Proyecto invalido.");

            var isMember = await _db.ProjectMembers.AnyAsync(m => m.ProjectId == project.Id && m.UserId == userId);
            var isOwner = project.OwnerId == userId;
            if (!isMember && !isOwner) return Forbid();

            areaId = project.AreaId;

            // Si el proyecto vive en una carpeta compartida y no se
            // eligio a nadie a mano, se asume que la tarea es para la
            // persona con la que se comparte la carpeta (igual se puede
            // cambiar despues, o elegir a otra persona desde el form).
            if (assignedToId is null && project.Folder?.SharedWithId is not null)
            {
                assignedToId = project.Folder.SharedWithId;
            }
        }
        else
        {
            // Tarea suelta: hereda el area del creador
            areaId = creator.AreaId;
        }

        if (assignedToId is not null)
        {
            var assignee = await _db.Users.FindAsync(assignedToId);
            if (assignee is null || assignee.AreaId != areaId)
                return BadRequest("El usuario asignado debe pertenecer a la misma area.");
        }

        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            ParentTaskId = dto.ParentTaskId,
            Title = dto.Title,
            Description = dto.Description,
            AreaId = areaId,
            CreatedById = userId,
            AssignedToId = assignedToId,
            Status = assignedToId is not null ? TaskItemStatus.Asignada : TaskItemStatus.Creada,
            FechaAsignacion = assignedToId is not null ? DateTime.UtcNow : null,
            FechaLimite = dto.FechaLimite,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _db.Tasks.Add(task);

        _db.TaskStatusHistories.Add(new TaskStatusHistory
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            ChangedById = userId,
            OldStatus = null,
            NewStatus = task.Status,
            ChangedAt = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync();

        var full = await _db.Tasks
            .Include(t => t.Project)
            .ThenInclude(p => p!.Folder)
            .Include(t => t.CreatedBy)
            .Include(t => t.AssignedTo)
            .Include(t => t.StatusHistory)
            .FirstAsync(t => t.Id == task.Id);

        // Recien creada: no puede tener comentarios ni adjuntos todavia.
        var resultDto = ToDto(full, ConteosTarea.Vacio);
        await NotificarCambio(full, resultDto);

        // Nace ya asignada (se elegio a alguien en el form de creacion) -
        // avisar por correo/Chat, best-effort (ver TaskNotificationService).
        await AvisarAsignacionAsync(full, dto.NotificarEn);

        return CreatedAtAction(nameof(GetByProject), new { projectId }, resultDto);
    }

    // PATCH /api/tasks/{id}/status
    // Registra el cambio en TaskStatusHistory y actualiza las fechas
    // correspondientes segun el nuevo estado.
    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<TaskItemDto>> UpdateStatus(Guid id, UpdateTaskStatusDto dto)
    {
        var userId = GetUserIdFromToken();

        // Include(StatusHistory): al agregar el nuevo registro mas abajo
        // via _db.TaskStatusHistories.Add(...), EF hace "fixup" del
        // navigation ya cargado, asi que ToDto(task) ve el cambio sin
        // necesidad de volver a consultar despues de SaveChanges.
        var task = await _db.Tasks
            .Include(t => t.Project)
            .ThenInclude(p => p!.Folder)
            // Owner del proyecto y de la oficina: solo hacen falta para
            // los avisos de Atendida/VolverARevisar (ver mas abajo,
            // TaskNotificationService.PersonasImportantes).
            .Include(t => t.Project)
            .ThenInclude(p => p!.Owner)
            .Include(t => t.Project)
            .ThenInclude(p => p!.Folder)
            .ThenInclude(f => f!.Owner)
            .Include(t => t.CreatedBy)
            .Include(t => t.AssignedTo)
            .Include(t => t.StatusHistory)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (task is null) return NotFound();

        // Visibilidad: cualquier miembro/dueno del proyecto (o el
        // creador/asignado, si es tarea suelta) puede ver la tarea. Que
        // pueda verla no implica que pueda moverla a cualquier estado
        // (ver el switch de abajo).
        if (task.ProjectId.HasValue)
        {
            var isMember = await _db.ProjectMembers.AnyAsync(m => m.ProjectId == task.ProjectId && m.UserId == userId);
            var isOwner = task.Project!.OwnerId == userId;
            if (!isMember && !isOwner) return Forbid();
        }
        else if (task.AssignedToId != userId && task.CreatedById != userId)
        {
            return Forbid();
        }

        // Quien crea la tarea decide el flujo; quien la tiene asignada
        // solo puede marcar su propio avance. El dueno del proyecto
        // cuenta como creador aunque no haya creado esa tarea puntual
        // (es quien invito a todos, tiene el mismo criterio de control).
        //
        // - Creada/Asignada/Leida son automaticas (se ponen solas al
        //   crear, asignar, o abrir el modal — ver Create/UpdateAssignee
        //   /MarkRead). Solo el creador las puede forzar a mano.
        // - En atencion/Atendida/Volver a revisar/Pausada: el ida-y-vuelta
        //   del avance del trabajo, lo marca quien la tiene asignada (o el
        //   creador) - el creador la manda a "Volver a revisar" cuando
        //   no quedo bien, y el asignado necesita poder mandarla de
        //   vuelta el mismo camino despues de corregirla (no solo saltar
        //   a Atendida), sin que el creador tenga que intervenir. Pausada
        //   entra en el mismo grupo PARA ENTRAR (cualquiera de los dos
        //   puede pausarla) - pero UNA VEZ pausada, la tarea queda
        //   congelada (ver el "if" de abajo): la unica interaccion que
        //   sigue permitida en toda la tarea es que el creador la
        //   reanude, ni el asignado puede moverla desde ahi. Mismo
        //   criterio en UpdateAssignee/UpdateDetails/
        //   TaskCommentsController/TaskAttachmentsController, que
        //   bloquean cualquier cambio mientras esta pausada.
        // - Terminada/Cancelada: cerrar el trabajo es decision de quien
        //   creo la tarea.
        var esCreador = task.CreatedById == userId
            || (task.ProjectId.HasValue && task.Project!.OwnerId == userId);
        var esAsignado = task.AssignedToId == userId;
        var estabaPausada = task.Status == TaskItemStatus.Pausada;

        var permitido = estabaPausada
            ? esCreador
            : dto.Status switch
            {
                TaskItemStatus.Creada or TaskItemStatus.Asignada or TaskItemStatus.Leida => esCreador,
                TaskItemStatus.EnAtencion or TaskItemStatus.Atendida or TaskItemStatus.VolverARevisar or TaskItemStatus.Pausada => esCreador || esAsignado,
                TaskItemStatus.Terminada or TaskItemStatus.Cancelada => esCreador,
                _ => false,
            };

        if (!permitido) return Forbid();

        task.UpdatedAt = DateTime.UtcNow;

        if (dto.Status == TaskItemStatus.EnAtencion && task.FechaAtencion is null)
            task.FechaAtencion = DateTime.UtcNow;

        if (dto.Status == TaskItemStatus.Terminada)
            task.FechaTerminacion = DateTime.UtcNow;

        CambiarEstado(task, userId, dto.Status);

        // Atendida con nota: el comentario se guarda en la misma
        // transaccion que el cambio de estado, y el aviso de mas abajo lo
        // incluye (un solo correo/Chat en lugar de dos).
        TaskComment? comentario = null;
        var textoComentario = dto.Status == TaskItemStatus.Atendida ? dto.Comment?.Trim() : null;
        if (!string.IsNullOrEmpty(textoComentario))
        {
            if (textoComentario.Length > MaxCommentLength)
                return BadRequest($"El comentario excede el limite de {MaxCommentLength} caracteres.");

            comentario = new TaskComment
            {
                Id = Guid.NewGuid(),
                TaskId = task.Id,
                UserId = userId,
                Content = textoComentario,
                CreatedAt = DateTime.UtcNow,
            };
            _db.TaskComments.Add(comentario);
        }

        await _db.SaveChangesAsync();

        if (comentario is not null)
        {
            var autor = await _db.Users.FirstAsync(u => u.Id == userId);
            await NotificarEvento(task, new TaskCommentDto(
                comentario.Id, comentario.TaskId, comentario.UserId,
                autor.FullName, comentario.Content, comentario.CreatedAt), "CommentAdded");
        }

        // Se reanuda: avisar a quien la tiene asignada (si sigue
        // habiendo alguien asignado) que ya puede volver a trabajar en
        // ella. Best-effort, igual que el aviso de asignacion.
        if (estabaPausada && dto.Status != TaskItemStatus.Pausada && task.AssignedTo is not null)
        {
            await _notifications.NotifyTaskResumedAsync(task, task.AssignedTo);
        }

        // Leida/Atendida: avisa a las "personas importantes" (creador,
        // dueno del proyecto, dueno de la oficina - deduplicadas, ver
        // PersonasImportantes). Leida normalmente llega por MarkRead (ver
        // ese metodo, que dispara el mismo aviso al abrir el modal), pero
        // el creador tambien la puede forzar a mano desde aca (ver el
        // switch de permisos de arriba) - se cubre igual. userId (quien
        // disparo el cambio) se excluye para no autonotificarse.
        if (dto.Status == TaskItemStatus.Leida)
        {
            await _notifications.NotifyTaskReadAsync(task, userId);
        }

        if (dto.Status == TaskItemStatus.Atendida)
        {
            await _notifications.NotifyTaskAttendedAsync(task, userId, comentario?.Content);
        }

        // Volver a revisar: aviso inverso, para quien tiene la tarea
        // asignada (no para "las personas importantes" - son ellas
        // quienes piden la revision). Si quien la manda a revisar es la
        // propia persona asignada (ver comentario del switch de
        // permisos, arriba), no hace falta avisarle a si misma.
        if (dto.Status == TaskItemStatus.VolverARevisar && task.AssignedTo is not null && task.AssignedToId != userId)
        {
            await _notifications.NotifyTaskNeedsReviewAsync(task, task.AssignedTo);
        }

        var resultDto = ToDto(task, await CargarConteos([task.Id]));
        await NotificarCambio(task, resultDto);

        return Ok(resultDto);
    }

    // GET /api/tasks/{id}/status-history
    // Linea de tiempo completa de cambios de estado (quien y cuando),
    // mas reciente primero. Independiente de quien tenga la tarea
    // asignada ahora mismo: ChangedById es un dato fijo del momento en
    // que ocurrio cada cambio, reasignar la tarea despues no lo toca
    // (ver comentario en TaskStatusHistory.cs).
    [HttpGet("{id:guid}/status-history")]
    public async Task<ActionResult<IEnumerable<TaskStatusHistoryDto>>> GetStatusHistory(Guid id)
    {
        var userId = GetUserIdFromToken();

        var task = await _db.Tasks
            .Include(t => t.Project)
            .ThenInclude(p => p!.Folder)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (task is null) return NotFound();

        // Misma regla de visibilidad que UpdateStatus/GetByProject.
        if (task.ProjectId.HasValue)
        {
            var isMember = await _db.ProjectMembers.AnyAsync(m => m.ProjectId == task.ProjectId && m.UserId == userId);
            var isOwner = task.Project!.OwnerId == userId;
            if (!isMember && !isOwner) return Forbid();
        }
        else if (task.AssignedToId != userId && task.CreatedById != userId)
        {
            return Forbid();
        }

        var historial = await _db.TaskStatusHistories
            .Include(h => h.ChangedBy)
            .Where(h => h.TaskId == id)
            .OrderByDescending(h => h.ChangedAt)
            .Select(h => new TaskStatusHistoryDto(
                h.Id,
                h.OldStatus,
                h.NewStatus,
                h.ChangedById,
                h.ChangedBy.FullName,
                h.ChangedAt
            ))
            .ToListAsync();

        return Ok(historial);
    }

    // Centraliza "mover el estado + registrar el historial": lo usan
    // UpdateStatus, MarkRead y UpdateAssignee (las transiciones
    // automaticas Asignada/Creada tambien quedan en TaskStatusHistory,
    // no solo las manuales).
    private void CambiarEstado(TaskItem task, string userId, TaskItemStatus nuevo)
    {
        var anterior = task.Status;
        task.Status = nuevo;

        _db.TaskStatusHistories.Add(new TaskStatusHistory
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            ChangedById = userId,
            OldStatus = anterior,
            NewStatus = nuevo,
            ChangedAt = DateTime.UtcNow,
        });
    }

    // PATCH /api/tasks/{id}/read
    // Transicion automatica Asignada -> Leida: el frontend la llama al
    // abrir el modal de detalle, no es una accion que el usuario elija
    // a mano (por eso no pasa por UpdateStatus ni por sus reglas de
    // permiso). Solo la dispara la propia persona asignada; idempotente
    // si ya esta en Leida o mas adelante en el flujo (no hace nada,
    // simplemente devuelve la tarea tal cual).
    [HttpPatch("{id:guid}/read")]
    public async Task<ActionResult<TaskItemDto>> MarkRead(Guid id)
    {
        var userId = GetUserIdFromToken();

        var task = await _db.Tasks
            .Include(t => t.Project)
            .ThenInclude(p => p!.Folder)
            // Owner del proyecto y de la oficina: solo hacen falta aca
            // para el aviso de "se leyo la tarea" (ver
            // TaskNotificationService.PersonasImportantes), el resto del
            // metodo no los usa.
            .Include(t => t.Project)
            .ThenInclude(p => p!.Owner)
            .Include(t => t.Project)
            .ThenInclude(p => p!.Folder)
            .ThenInclude(f => f!.Owner)
            .Include(t => t.CreatedBy)
            .Include(t => t.AssignedTo)
            .Include(t => t.StatusHistory)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (task is null) return NotFound();
        if (task.AssignedToId != userId) return Forbid();

        // Una sola vez para las dos llamadas a ToDto: marcar como leida no
        // toca comentarios ni adjuntos.
        var conteos = await CargarConteos([task.Id]);
        var resultDto = ToDto(task, conteos);

        if (task.Status == TaskItemStatus.Asignada)
        {
            task.UpdatedAt = DateTime.UtcNow;
            CambiarEstado(task, userId, TaskItemStatus.Leida);
            await _db.SaveChangesAsync();

            resultDto = ToDto(task, conteos);
            await NotificarCambio(task, resultDto);

            // Avisa a las "personas importantes" (creador, dueno del
            // proyecto, dueno de la oficina - deduplicadas, ver
            // PersonasImportantes) que quien la tiene asignada ya la
            // abrio. userId (quien la leyo) se excluye para no
            // autonotificarse en el caso, comun, de que sea la misma
            // persona en mas de un rol.
            await _notifications.NotifyTaskReadAsync(task, userId);
        }

        return Ok(resultDto);
    }

    // PATCH /api/tasks/{id}/assign
    // Asigna (o quita la asignacion, si AssignedToId viene null) una
    // tarea ya creada. El nuevo asignado debe pertenecer a la misma
    // area de la tarea, igual que en Create. Solo quien creo la tarea
    // puede llamar este endpoint (ver chequeo de abajo).
    [HttpPatch("{id:guid}/assign")]
    public async Task<ActionResult<TaskItemDto>> UpdateAssignee(Guid id, UpdateTaskAssigneeDto dto)
    {
        var userId = GetUserIdFromToken();
        var assignedToId = dto.AssignedToId?.ToLowerInvariant(); // ver comentario en User.Id

        var task = await _db.Tasks
            .Include(t => t.Project)
            .ThenInclude(p => p!.Folder)
            .Include(t => t.CreatedBy)
            .Include(t => t.AssignedTo)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (task is null) return NotFound();
        if (task.Status == TaskItemStatus.Pausada)
            return Conflict("La tarea esta pausada. Reanudala antes de reasignarla.");

        // A diferencia de UpdateStatus/UpdateDetails, asignar (o
        // reasignar) una tarea es exclusivo de quien la creo: ni el
        // dueno del proyecto, ni la persona asignada actualmente, pueden
        // cambiarla por otra.
        if (task.CreatedById != userId) return Forbid();

        if (assignedToId is not null)
        {
            var assignee = await _db.Users.FindAsync(assignedToId);
            if (assignee is null || assignee.AreaId != task.AreaId)
                return BadRequest("El usuario asignado debe pertenecer a la misma area.");
        }

        var asignadoAnterior = task.AssignedToId;
        var assigneeCambio = task.AssignedToId != assignedToId;
        var esEstadoCerrado = task.Status == TaskItemStatus.Terminada || task.Status == TaskItemStatus.Cancelada;

        task.AssignedToId = assignedToId;
        task.UpdatedAt = DateTime.UtcNow;

        // Si habia un aviso de asignacion programado y todavia no salio,
        // ya no aplica: era para el asignado anterior. Se cancela en el
        // mismo SaveChanges de abajo.
        if (assigneeCambio)
        {
            var pendientes = await _db.ScheduledTaskNotifications
                .Where(n => n.TaskId == task.Id && n.SentAt == null && n.CancelledAt == null)
                .ToListAsync();
            foreach (var pendiente in pendientes)
            {
                pendiente.CancelledAt = DateTime.UtcNow;
            }
        }

        if (assignedToId is not null)
        {
            task.FechaAsignacion = DateTime.UtcNow;

            // Asignar (primera vez o reasignar a otra persona) reinicia
            // el flujo de lectura: quien recibe la tarea ahora todavia
            // no la ha abierto, sin importar que tan lejos hubiera
            // llegado con el asignado anterior. No se toca si es
            // "reasignar" a la misma persona que ya la tenia, ni si la
            // tarea ya esta cerrada (Terminada/Cancelada) — eso no
            // deberia revivirla a medio camino.
            if (assigneeCambio && !esEstadoCerrado)
            {
                CambiarEstado(task, userId, TaskItemStatus.Asignada);
            }
        }
        else if (assigneeCambio && !esEstadoCerrado)
        {
            // Se quito la asignacion: vuelve a "Creada", como si nunca
            // se hubiera asignado.
            CambiarEstado(task, userId, TaskItemStatus.Creada);
        }

        await _db.SaveChangesAsync();

        var full = await _db.Tasks
            .Include(t => t.Project)
            .ThenInclude(p => p!.Folder)
            .Include(t => t.CreatedBy)
            .Include(t => t.AssignedTo)
            .Include(t => t.StatusHistory)
            .FirstAsync(t => t.Id == task.Id);

        var resultDto = ToDto(full, await CargarConteos([full.Id]));
        var avisarAsignadoAnterior = assigneeCambio && asignadoAnterior is not null ? asignadoAnterior : null;
        await NotificarCambio(full, resultDto, avisarAsignadoAnterior);

        // Solo si de verdad cambio el asignado (no en un "reasignar" a la
        // misma persona) y quedo alguien asignado (no en un "quitar
        // asignacion").
        if (assigneeCambio)
        {
            await AvisarAsignacionAsync(full, dto.NotificarEn);
        }

        return Ok(resultDto);
    }

    // Avisa de la asignacion: al instante, o encolado si NotificarEn es
    // una hora futura (lo manda ScheduledNotificationWorker). Sin asignado
    // no hace nada. Una fecha pasada o de "ahora mismo" se trata como
    // aviso inmediato, para no dejar en cola algo que ya vencio.
    private async Task AvisarAsignacionAsync(TaskItem task, DateTime? notificarEn)
    {
        if (task.AssignedTo is null) return;

        if (notificarEn is { } cuando)
        {
            // Un Kind=Unspecified (JSON sin "Z") se asume UTC, igual que
            // el resto de fechas del proyecto (ver AppDbContext).
            var sendAt = cuando.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(cuando, DateTimeKind.Utc)
                : cuando.ToUniversalTime();

            if (sendAt > DateTime.UtcNow)
            {
                _db.ScheduledTaskNotifications.Add(new ScheduledTaskNotification
                {
                    Id = Guid.NewGuid(),
                    TaskId = task.Id,
                    RecipientId = task.AssignedTo.Id,
                    SendAt = sendAt,
                    CreatedAt = DateTime.UtcNow,
                });
                await _db.SaveChangesAsync();
                return;
            }
        }

        await _notifications.NotifyTaskAssignedAsync(task, task.AssignedTo);
    }

    // PATCH /api/tasks/{id}/details
    // Edita titulo y descripcion. Mismo criterio de autorizacion que
    // UpdateStatus/UpdateAssignee: miembro/dueno del proyecto, o
    // creador/asignado si es tarea suelta.
    [HttpPatch("{id:guid}/details")]
    public async Task<ActionResult<TaskItemDto>> UpdateDetails(Guid id, UpdateTaskDetailsDto dto)
    {
        var userId = GetUserIdFromToken();

        var task = await _db.Tasks
            .Include(t => t.Project)
            .ThenInclude(p => p!.Folder)
            .Include(t => t.CreatedBy)
            .Include(t => t.AssignedTo)
            .Include(t => t.StatusHistory)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (task is null) return NotFound();
        if (task.Status == TaskItemStatus.Pausada)
            return Conflict("La tarea esta pausada. Reanudala antes de editarla.");

        if (task.ProjectId.HasValue)
        {
            var isMember = await _db.ProjectMembers.AnyAsync(m => m.ProjectId == task.ProjectId && m.UserId == userId);
            var isOwner = task.Project!.OwnerId == userId;
            if (!isMember && !isOwner) return Forbid();
        }
        else if (task.AssignedToId != userId && task.CreatedById != userId)
        {
            return Forbid();
        }

        var title = dto.Title?.Trim() ?? string.Empty;
        if (title.Length == 0) return BadRequest("El titulo de la tarea no puede estar vacio.");

        task.Title = title;
        task.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description;
        task.FechaLimite = dto.FechaLimite;
        task.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        // Igual que UpdateStatus/UpdateAssignee: el DTO sale sin
        // subtareas y el frontend conserva las que ya tenia cargadas.
        var resultDto = ToDto(task, await CargarConteos([task.Id]));
        await NotificarCambio(task, resultDto);

        return Ok(resultDto);
    }

    // DELETE /api/tasks/{id}
    // Borrado logico (ver TaskItem.IsDeleted): nunca se quita de la BD,
    // solo se marca y desaparece de todas las vistas normales gracias al
    // HasQueryFilter global en AppDbContext. Cascada de un nivel: sus
    // subtareas (si tiene) se borran con ella, para no dejar hijas
    // huerfanas colgando de un padre que ya no aparece en ningun lado -
    // se restauran juntas tambien (ver Restore).
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = GetUserIdFromToken();

        var task = await _db.Tasks
            .Include(t => t.Project)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (task is null) return NotFound();
        if (!EsCreador(task, userId)) return Forbid();

        var ahora = DateTime.UtcNow;
        var subtareas = await _db.Tasks.Where(t => t.ParentTaskId == id).ToListAsync();

        foreach (var sub in subtareas)
        {
            sub.IsDeleted = true;
            sub.DeletedAt = ahora;
            sub.DeletedById = userId;
            sub.UpdatedAt = ahora;
        }

        task.IsDeleted = true;
        task.DeletedAt = ahora;
        task.DeletedById = userId;
        task.UpdatedAt = ahora;

        await _db.SaveChangesAsync();

        // Evento propio (no TaskChanged): quien tiene la lista abierta
        // debe QUITAR la fila, no actualizarla con datos que ya no
        // deberia poder ver.
        await NotificarEvento(task, new TaskDeletedDto(task.Id, task.ParentTaskId, task.ProjectId), "TaskDeleted");

        return NoContent();
    }

    // GET /api/tasks/trash?projectId=xxx
    // projectId presente: papelera de ESE proyecto (mismo criterio de
    // visibilidad que GetByProject - cualquier miembro/dueno, no solo
    // quien borro cada tarea). projectId ausente: papelera de tareas
    // sueltas, solo las que el usuario actual creo (unica gente que
    // pudo haberlas borrado, ver EsCreador para tareas sin proyecto).
    [HttpGet("trash")]
    public async Task<ActionResult<IEnumerable<TaskTrashItemDto>>> GetTrash([FromQuery] Guid? projectId)
    {
        var userId = GetUserIdFromToken();

        var query = _db.Tasks.IgnoreQueryFilters().Where(t => t.IsDeleted);

        if (projectId.HasValue)
        {
            var isMember = await _db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == userId);
            var isOwner = await _db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == userId);
            if (!isMember && !isOwner) return Forbid();

            query = query.Where(t => t.ProjectId == projectId);
        }
        else
        {
            query = query.Where(t => t.ProjectId == null && t.CreatedById == userId);
        }

        var papelera = await query
            .Include(t => t.Project)
            .Include(t => t.CreatedBy)
            .Include(t => t.AssignedTo)
            .Include(t => t.DeletedBy)
            .OrderByDescending(t => t.DeletedAt)
            .Select(t => new TaskTrashItemDto(
                t.Id,
                t.ProjectId,
                t.Project!.Name,
                t.ParentTaskId,
                t.Title,
                t.Status,
                t.CreatedBy.FullName,
                t.AssignedTo != null ? t.AssignedTo.FullName : null,
                t.DeletedAt!.Value,
                t.DeletedBy!.FullName
            ))
            .ToListAsync();

        return Ok(papelera);
    }

    // POST /api/tasks/{id}/restore
    // Deshace Delete: quita el marcado de borrada de la tarea y de las
    // subtareas que se hayan ido con ella (ver comentario en Delete).
    // No distingue si alguna subtarea se habia borrado por separado
    // antes que su padre - restaurar el padre restaura todo lo que
    // cuelgue de el, es el comportamiento mas predecible para quien
    // recupera algo desde la papelera.
    [HttpPost("{id:guid}/restore")]
    public async Task<ActionResult<TaskItemDto>> Restore(Guid id)
    {
        var userId = GetUserIdFromToken();

        var task = await _db.Tasks
            .IgnoreQueryFilters()
            .Include(t => t.Project)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (task is null) return NotFound();
        if (!task.IsDeleted) return BadRequest("Esa tarea no esta en la papelera.");
        if (!EsCreador(task, userId)) return Forbid();

        var subtareas = await _db.Tasks
            .IgnoreQueryFilters()
            .Where(t => t.ParentTaskId == id && t.IsDeleted)
            .ToListAsync();

        foreach (var sub in subtareas)
        {
            sub.IsDeleted = false;
            sub.DeletedAt = null;
            sub.DeletedById = null;
            sub.UpdatedAt = DateTime.UtcNow;
        }

        task.IsDeleted = false;
        task.DeletedAt = null;
        task.DeletedById = null;
        task.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var full = await _db.Tasks
            .Include(t => t.Project)
            .ThenInclude(p => p!.Folder)
            .Include(t => t.CreatedBy)
            .Include(t => t.AssignedTo)
            .Include(t => t.StatusHistory)
            .FirstAsync(t => t.Id == task.Id);

        var resultDto = ToDto(full, await CargarConteos([full.Id]));
        await NotificarCambio(full, resultDto);

        return Ok(resultDto);
    }

    // Cuantos comentarios/adjuntos tiene cada tarea, para los iconos de
    // la tabla. Dos GROUP BY en vez de Include(t => t.Comments/
    // .Attachments): con Include, cada renglon de la lista arrastraria el
    // texto completo de sus comentarios (hasta 4000 caracteres cada uno)
    // y los metadatos de sus archivos, cuando lo unico que se necesita es
    // saber si hay o no. Son 2 queries fijas, no una por tarea.
    private sealed record ConteosTarea(
        Dictionary<Guid, int> Comentarios,
        Dictionary<Guid, int> Adjuntos)
    {
        public static readonly ConteosTarea Vacio = new([], []);

        public int ComentariosDe(Guid taskId) => Comentarios.GetValueOrDefault(taskId);
        public int AdjuntosDe(Guid taskId) => Adjuntos.GetValueOrDefault(taskId);
    }

    private async Task<ConteosTarea> CargarConteos(IReadOnlyCollection<Guid> taskIds)
    {
        if (taskIds.Count == 0) return ConteosTarea.Vacio;

        var comentarios = await _db.TaskComments
            .Where(c => taskIds.Contains(c.TaskId))
            .GroupBy(c => c.TaskId)
            .Select(g => new { TaskId = g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.TaskId, x => x.Total);

        var adjuntos = await _db.TaskAttachments
            .Where(a => taskIds.Contains(a.TaskId))
            .GroupBy(a => a.TaskId)
            .Select(g => new { TaskId = g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.TaskId, x => x.Total);

        return new ConteosTarea(comentarios, adjuntos);
    }

    // subtasks: solo se pasa al mapear una tarea top-level (ver
    // GetByProject). Al mapear cada subtarea se llama sin este
    // parametro, con lo que su propio Subtasks queda vacio y se respeta
    // el limite de un solo nivel de anidamiento.
    private static TaskItemDto ToDto(
        TaskItem t,
        ConteosTarea conteos,
        IReadOnlyList<TaskItem>? subtasks = null) => new(
        t.Id,
        t.ProjectId,
        t.Project?.Name,
        t.Project?.Color,
        t.Project?.Folder?.Id,
        t.Project?.Folder?.Name,
        t.Project?.Folder?.Color,
        t.ParentTaskId,
        t.Title,
        t.Description,
        t.AreaId,
        t.CreatedById,
        t.CreatedBy.FullName,
        t.AssignedToId,
        t.AssignedTo?.FullName,
        t.Status,
        t.FechaAsignacion,
        t.FechaAtencion,
        t.FechaTerminacion,
        t.FechaLimite,
        t.CreatedAt,
        t.StatusHistory.Count > 0 ? t.StatusHistory.Max(h => h.ChangedAt) : t.CreatedAt,
        conteos.ComentariosDe(t.Id),
        conteos.AdjuntosDe(t.Id),
        (subtasks ?? Array.Empty<TaskItem>()).Select(s => ToDto(s, conteos)).ToList()
    );

    private string GetUserIdFromToken()
    {
        var sub = User.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("Token sin claim 'sub'.");

        // Normalizado a minusculas - ver comentario en User.Id.
        return sub.ToLowerInvariant();
    }
}