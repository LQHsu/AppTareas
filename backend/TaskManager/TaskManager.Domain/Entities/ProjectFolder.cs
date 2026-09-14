namespace TaskManager.Domain.Entities;

// Carpeta para organizar proyectos. Es personal: pertenece a un usuario
// y solo el la administra. Un proyecto vive en una sola carpeta (o en
// ninguna), ver Project.FolderId.
public class ProjectFolder
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    // Dueno de la carpeta. Solo el puede renombrarla, borrarla, meter o
    // sacar proyectos y compartirla.
    public string OwnerId { get; set; } = string.Empty;
    public User Owner { get; set; } = null!;

    // Con quien esta compartida (null = no compartida). Es UNA sola
    // persona a proposito: compartir reasigna todas las tareas de la
    // carpeta a este usuario, y TaskItem.AssignedToId solo admite uno.
    public string? SharedWithId { get; set; }
    public User? SharedWith { get; set; }

    public DateTime? SharedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<Project> Projects { get; set; } = new List<Project>();
}
