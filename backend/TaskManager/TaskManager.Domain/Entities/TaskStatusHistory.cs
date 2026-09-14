using TaskManager.Domain.Enums;
 
namespace TaskManager.Domain.Entities;
 
// Bitacora de cambios de estado. Util para auditoria y para mostrar
// una linea de tiempo de la tarea en el frontend (igual que el
// historial de actividad que muestra Jira en cada issue).
public class TaskStatusHistory
{
    public Guid Id { get; set; }
 
    public Guid TaskId { get; set; }
    public TaskItem Task { get; set; } = null!;
 
    public string ChangedById { get; set; } = string.Empty;
    public User ChangedBy { get; set; } = null!;
 
    public TaskItemStatus? OldStatus { get; set; }
    public TaskItemStatus NewStatus { get; set; }
 
    public DateTime ChangedAt { get; set; }
}
 