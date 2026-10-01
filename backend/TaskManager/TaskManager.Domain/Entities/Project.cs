namespace TaskManager.Domain.Entities;
 
public class Project
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Color elegido por el dueno para distinguir el proyecto (hex, ej.
    // "#3B82F6"). Independiente del color de Folder: no se hereda.
    public string? Color { get; set; }

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

    // Borrado logico (papelera de proyectos, ver ProjectsController.Delete/
    // Restore). Solo se usa si el proyecto ya tuvo alguna tarea (incluidas
    // las de la papelera): uno que nunca tuvo tareas se borra de verdad.
    // Filtrado por un HasQueryFilter global en AppDbContext, que ademas
    // oculta las tareas del proyecto - se recuperan al restaurarlo. Solo
    // el dueno puede borrar, asi que no hace falta guardar quien fue.
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
 
    public ICollection<ProjectMember> Members { get; set; } = new List<ProjectMember>();
    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
 
    // El % de completado NO se guarda en columna; se calcula en el
    // servicio de aplicacion como Terminadas / (Total - Canceladas)
    // para no tener que mantenerlo sincronizado en cada cambio de estado.
}
 