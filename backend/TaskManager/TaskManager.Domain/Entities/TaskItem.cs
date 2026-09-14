using TaskManager.Domain.Enums;
 
namespace TaskManager.Domain.Entities;
 
public class TaskItem
{
    public Guid Id { get; set; }
 
    // Nullable: una tarea puede no pertenecer a ningun proyecto
    // (tarea "suelta" asignada dentro de la misma area).
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }
 
    // Auto-referencia para subtareas (adjacency list).
    // Se lee con un CTE recursivo cuando se necesita el arbol completo.
    public Guid? ParentTaskId { get; set; }
    public TaskItem? ParentTask { get; set; }
    public ICollection<TaskItem> SubTasks { get; set; } = new List<TaskItem>();
 
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
 
    // Redundante respecto a Project.AreaId, pero necesario para
    // tareas sin proyecto (asi se sabe a que area pertenecen
    // y quien puede verlas/asignarlas).
    public int AreaId { get; set; }
    public Area Area { get; set; } = null!;
 
    public Guid CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;
 
    public Guid? AssignedToId { get; set; }
    public User? AssignedTo { get; set; }
 
    public TaskItemStatus Status { get; set; } = TaskItemStatus.Creada;
 
    public DateTime? FechaAsignacion { get; set; }
    public DateTime? FechaAtencion { get; set; }
    public DateTime? FechaTerminacion { get; set; }
 
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
 
    public ICollection<TaskComment> Comments { get; set; } = new List<TaskComment>();
    public ICollection<TaskAttachment> Attachments { get; set; } = new List<TaskAttachment>();
    public ICollection<TaskStatusHistory> StatusHistory { get; set; } = new List<TaskStatusHistory>();
}