using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.DTOs;
using TaskManager.Api.Hubs;
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
    private readonly IHubContext<TaskHub> _hub;

    public TasksController(AppDbContext db, IHubContext<TaskHub> hub)
    {
        _db = db;
        _hub = hub;
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
    private Task NotificarCambio(TaskItem task, TaskItemDto dto, string? avisarTambienA = null)
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

        return Task.WhenAll(grupos.Select(g => _hub.Clients.Group(g).SendAsync("TaskChanged", dto)));
    }

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

        var topLevel = allTasks
            .Where(t => t.ParentTaskId is null)
            .Select(t => ToDto(t, subtasksByParent.GetValueOrDefault(t.Id)))
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
            .Include(t => t.CreatedBy)
            .Include(t => t.AssignedTo)
            .Include(t => t.StatusHistory)
            .Where(t => t.AssignedToId == userId || t.CreatedById == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        return Ok(tasks.Select(t => ToDto(t)).ToList());
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
                .FirstOrDefaultAsync(t => t.Id == dto.ParentTaskId.Value);

            if (parent is null) return BadRequest("Tarea padre invalida.");
            if (parent.ParentTaskId.HasValue)
                return BadRequest("No se pueden anidar subtareas de subtareas (solo se permite un nivel).");

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
            var project = await _db.Projects.FindAsync(dto.ProjectId.Value);
            if (project is null) return BadRequest("Proyecto invalido.");

            var isMember = await _db.ProjectMembers.AnyAsync(m => m.ProjectId == project.Id && m.UserId == userId);
            var isOwner = project.OwnerId == userId;
            if (!isMember && !isOwner) return Forbid();

            areaId = project.AreaId;
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
            .Include(t => t.CreatedBy)
            .Include(t => t.AssignedTo)
            .Include(t => t.StatusHistory)
            .FirstAsync(t => t.Id == task.Id);

        var resultDto = ToDto(full);
        await NotificarCambio(full, resultDto);

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
        // - En atencion/Atendida: el avance del trabajo en si, lo marca
        //   quien la tiene asignada (o el creador).
        // - Volver a revisar/Terminada/Cancelada: evaluar y cerrar el
        //   trabajo es decision de quien creo la tarea.
        var esCreador = task.CreatedById == userId
            || (task.ProjectId.HasValue && task.Project!.OwnerId == userId);
        var esAsignado = task.AssignedToId == userId;

        var permitido = dto.Status switch
        {
            TaskItemStatus.Creada or TaskItemStatus.Asignada or TaskItemStatus.Leida => esCreador,
            TaskItemStatus.EnAtencion or TaskItemStatus.Atendida => esCreador || esAsignado,
            TaskItemStatus.VolverARevisar or TaskItemStatus.Terminada or TaskItemStatus.Cancelada => esCreador,
            _ => false,
        };

        if (!permitido) return Forbid();

        task.UpdatedAt = DateTime.UtcNow;

        if (dto.Status == TaskItemStatus.EnAtencion && task.FechaAtencion is null)
            task.FechaAtencion = DateTime.UtcNow;

        if (dto.Status == TaskItemStatus.Terminada)
            task.FechaTerminacion = DateTime.UtcNow;

        CambiarEstado(task, userId, dto.Status);

        await _db.SaveChangesAsync();

        var resultDto = ToDto(task);
        await NotificarCambio(task, resultDto);

        return Ok(resultDto);
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
            .Include(t => t.CreatedBy)
            .Include(t => t.AssignedTo)
            .Include(t => t.StatusHistory)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (task is null) return NotFound();
        if (task.AssignedToId != userId) return Forbid();

        var resultDto = ToDto(task);

        if (task.Status == TaskItemStatus.Asignada)
        {
            task.UpdatedAt = DateTime.UtcNow;
            CambiarEstado(task, userId, TaskItemStatus.Leida);
            await _db.SaveChangesAsync();

            resultDto = ToDto(task);
            await NotificarCambio(task, resultDto);
        }

        return Ok(resultDto);
    }

    // PATCH /api/tasks/{id}/assign
    // Asigna (o quita la asignacion, si AssignedToId viene null) una
    // tarea ya creada. El nuevo asignado debe pertenecer a la misma
    // area de la tarea, igual que en Create.
    [HttpPatch("{id:guid}/assign")]
    public async Task<ActionResult<TaskItemDto>> UpdateAssignee(Guid id, UpdateTaskAssigneeDto dto)
    {
        var userId = GetUserIdFromToken();
        var assignedToId = dto.AssignedToId?.ToLowerInvariant(); // ver comentario en User.Id

        var task = await _db.Tasks
            .Include(t => t.Project)
            .Include(t => t.CreatedBy)
            .Include(t => t.AssignedTo)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (task is null) return NotFound();

        // Mismo criterio de autorizacion que UpdateStatus: cualquier
        // miembro/dueno del proyecto, o el creador/asignado si es suelta.
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
            .Include(t => t.CreatedBy)
            .Include(t => t.AssignedTo)
            .Include(t => t.StatusHistory)
            .FirstAsync(t => t.Id == task.Id);

        var resultDto = ToDto(full);
        var avisarAsignadoAnterior = assigneeCambio && asignadoAnterior is not null ? asignadoAnterior : null;
        await NotificarCambio(full, resultDto, avisarAsignadoAnterior);

        return Ok(resultDto);
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
            .Include(t => t.CreatedBy)
            .Include(t => t.AssignedTo)
            .Include(t => t.StatusHistory)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (task is null) return NotFound();

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
        task.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        // Igual que UpdateStatus/UpdateAssignee: el DTO sale sin
        // subtareas y el frontend conserva las que ya tenia cargadas.
        var resultDto = ToDto(task);
        await NotificarCambio(task, resultDto);

        return Ok(resultDto);
    }

    // subtasks: solo se pasa al mapear una tarea top-level (ver
    // GetByProject). Al mapear cada subtarea se llama sin este
    // parametro, con lo que su propio Subtasks queda vacio y se respeta
    // el limite de un solo nivel de anidamiento.
    private static TaskItemDto ToDto(TaskItem t, IReadOnlyList<TaskItem>? subtasks = null) => new(
        t.Id,
        t.ProjectId,
        t.Project?.Name,
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
        t.CreatedAt,
        t.StatusHistory.Count > 0 ? t.StatusHistory.Max(h => h.ChangedAt) : t.CreatedAt,
        (subtasks ?? Array.Empty<TaskItem>()).Select(s => ToDto(s)).ToList()
    );

    private string GetUserIdFromToken()
    {
        var sub = User.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("Token sin claim 'sub'.");

        // Normalizado a minusculas - ver comentario en User.Id.
        return sub.ToLowerInvariant();
    }
}