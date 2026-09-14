namespace TaskManager.Domain.Entities;
 
// Tabla intermedia para la relacion muchos-a-muchos Project <-> User.
// Un usuario invitado a un proyecto debe pertenecer a la misma area
// que el proyecto (validacion en el servicio de aplicacion).
public class ProjectMember
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
 
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
 
    public DateTime JoinedAt { get; set; }
}
 