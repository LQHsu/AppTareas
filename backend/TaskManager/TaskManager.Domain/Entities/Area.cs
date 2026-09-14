namespace TaskManager.Domain.Entities;
 
// Representa un area institucional. El dato "maestro" vive en la BD
// institucional; esta tabla es una copia local de solo lectura,
// sincronizada periodicamente, para poder hacer joins eficientes
// con Project y TaskItem sin depender de una llamada externa en cada request.
public class Area
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
 
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Project> Projects { get; set; } = new List<Project>();
    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
}
 