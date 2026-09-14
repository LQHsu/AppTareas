using TaskManager.Domain.Enums;

namespace TaskManager.Api.DTOs;

// Subtasks: solo un nivel de anidamiento (Subtasks de un elemento en
// Subtasks siempre viene vacio). Se aplana en el controller, no requiere
// query recursiva mientras se mantenga esa regla.
public record TaskItemDto(
    Guid Id,
    Guid? ProjectId,
    string? ProjectName,
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
    DateTime CreatedAt,
    // Fecha del cambio de estado mas reciente (max de TaskStatusHistory,
    // o CreatedAt si por alguna razon no hay historial todavia).
    DateTime LastStatusChangeAt,
    IReadOnlyList<TaskItemDto> Subtasks
);

// ProjectId nulo = tarea suelta, asignada solo dentro del area.
public record CreateTaskDto(
    string Title,
    string? Description,
    Guid? ProjectId,
    string? AssignedToId,
    Guid? ParentTaskId
);

public record UpdateTaskStatusDto(TaskItemStatus Status);

// AssignedToId nulo = quitar la asignacion (vuelve a quedar sin asignar).
public record UpdateTaskAssigneeDto(string? AssignedToId);

// Description viene como HTML (lo produce app-editor-texto).
public record UpdateTaskDetailsDto(string Title, string? Description);