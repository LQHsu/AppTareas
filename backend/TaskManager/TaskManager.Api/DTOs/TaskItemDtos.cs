using TaskManager.Domain.Enums;

namespace TaskManager.Api.DTOs;

// Subtasks: solo un nivel de anidamiento (Subtasks de un elemento en
// Subtasks siempre viene vacio). Se aplana en el controller, no requiere
// query recursiva mientras se mantenga esa regla.
public record TaskItemDto(
    Guid Id,
    Guid? ProjectId,
    string? ProjectName,
    // Color del proyecto padre (hex), null si es tarea suelta o el
    // proyecto no tiene color. La tarea no tiene color propio.
    string? ProjectColor,
    // Oficina (carpeta) del proyecto padre - null si es tarea suelta, o
    // si el proyecto no esta en ninguna oficina. Se usa para poder
    // agrupar/filtrar tareas por oficina en vistas que mezclan varios
    // proyectos (ver mis-tareas).
    Guid? FolderId,
    string? FolderName,
    string? FolderColor,
    Guid? ParentTaskId,
    string Title,
    string? Description,
    int AreaId,
    string CreatedById,
    string CreatedByFullName,
    string? AssignedToId,
    string? AssignedToFullName,
    TaskItemStatus Status,
    DateTime? FechaAsignacion,
    DateTime? FechaAtencion,
    DateTime? FechaTerminacion,
    // Elegida a mano, puramente informativa (ver comentario en TaskItem.cs).
    DateTime? FechaLimite,
    DateTime CreatedAt,
    // Fecha del cambio de estado mas reciente (max de TaskStatusHistory,
    // o CreatedAt si por alguna razon no hay historial todavia).
    DateTime LastStatusChangeAt,
    // Solo para los iconos de "tiene adjuntos"/"tiene comentarios" en la
    // tabla de tareas: se cuentan con un GROUP BY aparte, NO cargando las
    // colecciones completas (el contenido de cada comentario puede llegar
    // a 4000 caracteres, y no se usa para nada en la lista).
    int CommentCount,
    int AttachmentCount,
    IReadOnlyList<TaskItemDto> Subtasks
);

// ProjectId nulo = tarea suelta, asignada solo dentro del area.
public record CreateTaskDto(
    string Title,
    string? Description,
    Guid? ProjectId,
    string? AssignedToId,
    Guid? ParentTaskId,
    DateTime? FechaLimite = null,
    // Si viene y esta en el futuro, el aviso de asignacion no sale al
    // instante: se encola y sale a esa hora (UTC). Nulo = aviso inmediato.
    DateTime? NotificarEn = null
);

public record UpdateTaskStatusDto(TaskItemStatus Status);

// AssignedToId nulo = quitar la asignacion (vuelve a quedar sin asignar).
// NotificarEn: igual que en CreateTaskDto.
public record UpdateTaskAssigneeDto(string? AssignedToId, DateTime? NotificarEn = null);

// Description viene como HTML (lo produce app-editor-texto). FechaLimite
// nula = sin fecha limite (tambien sirve para quitarsela a una que ya
// tenia).
public record UpdateTaskDetailsDto(string Title, string? Description, DateTime? FechaLimite = null);

// Una fila del historial de cambios de estado de una tarea. OldStatus
// nulo = la entrada de creacion (no habia estado previo). ChangedById
// es quien hizo ESE cambio puntual - no se reescribe si la tarea
// despues cambia de asignado, es un dato fijo del momento en que
// ocurrio (ver comentario en TaskStatusHistory.cs).
public record TaskStatusHistoryDto(
    Guid Id,
    TaskItemStatus? OldStatus,
    TaskItemStatus NewStatus,
    string ChangedById,
    string ChangedByFullName,
    DateTime ChangedAt
);

// Fila de la papelera (ver TasksController.GetTrash/Restore/DeletePermanently).
// DTO propio en vez de reusar TaskItemDto: la papelera no necesita
// subtareas/conteos/status-history, solo lo minimo para decidir "esta es
// la que borre por error, restaurarla" o "borrarla para siempre".
// Payload del evento SignalR "TaskDeleted" (ver TasksController.Delete):
// deliberadamente minimo, solo lo que necesita quien tiene la lista
// abierta para quitar la fila de encima sin tener que volver a pedir
// todo. ParentTaskId: si es una subtarea, el frontend sabe de que padre
// colgaba para quitarla de su lista de Subtasks en vez de la raiz.
public record TaskDeletedDto(Guid Id, Guid? ParentTaskId, Guid? ProjectId);

public record TaskTrashItemDto(
    Guid Id,
    Guid? ProjectId,
    string? ProjectName,
    Guid? ParentTaskId,
    string Title,
    TaskItemStatus Status,
    string CreatedByFullName,
    string? AssignedToFullName,
    DateTime DeletedAt,
    string DeletedByFullName
);
// Aviso de asignacion programado (ver ScheduledTaskNotification), para la
// pestana "Avisos programados" de Inicio. Estado = Pendiente (SentAt y
// CancelledAt nulos) o Enviado (SentAt con valor).
public record ScheduledNotificationDto(
    Guid Id,
    Guid TaskId,
    string TaskTitle,
    Guid? ProjectId,
    string? ProjectName,
    string RecipientFullName,
    DateTime SendAt,
    DateTime? SentAt
);
