namespace TaskManager.Domain.Entities;
 
public class Project
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
 
    // El proyecto pertenece a un area especifica; solo usuarios de esa
    // misma area pueden ser invitados como miembros (regla de negocio,
    // se valida en el servicio de aplicacion, no aqui).
    public int AreaId { get; set; }
    public Area Area { get; set; } = null!;
 
    public string OwnerId { get; set; } = string.Empty;
    public User Owner { get; set; } = null!;
 
    // Carpeta en la que esta el proyecto (null = sin carpeta). Es una
    // sola: las carpetas funcionan como carpetas de archivos, no como
    // etiquetas. La carpeta es personal del dueno del proyecto, por eso
    // solo se pueden meter proyectos propios (se valida en el controller).
    public Guid? FolderId { get; set; }
    public ProjectFolder? Folder { get; set; }

    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }
 
    public ICollection<ProjectMember> Members { get; set; } = new List<ProjectMember>();
    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
 
    // El % de completado NO se guarda en columna; se calcula en el
    // servicio de aplicacion como Terminadas / (Total - Canceladas)
    // para no tener que mantenerlo sincronizado en cada cambio de estado.
}
 