namespace TaskManager.Domain.Entities;
 
public class User
{
    // Debe coincidir con el "sub" (subject) del token JWT emitido por
    // Keycloak - string, no Guid: para un usuario federado (ver
    // CusxacdiUserStorageProvider en docker/keycloak/) Keycloak arma un
    // "sub" compuesto tipo "f:<uuid-del-provider>:<matricula>", no un GUID
    // puro. Normalizado siempre a minusculas por GetUserIdFromToken() en
    // cada controller, para que las comparaciones "==" no dependan de que
    // el token mande el mismo casing cada vez.
    public string Id { get; set; } = string.Empty;
 
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
 
    public int AreaId { get; set; }
    public Area Area { get; set; } = null!;
 
    // Fuente de verdad real (no un cache): es una columna propia de esta
    // BD, no esta ligada a ningun rol de Keycloak todavia (no existe
    // sincronizacion de realm roles en este proyecto). Siempre false al
    // auto-registrarse; se cambia via el panel de super admin, y el
    // primer super admin de un ambiente nuevo se siembra a mano en SQL.
    public bool IsSuperAdmin { get; set; }

    // Baneo a nivel app: Keycloak sigue emitiendo un JWT valido para este
    // usuario (no hay forma de revocar eso desde aca), asi que el bloqueo
    // real ocurre en BanCheckMiddleware, que corta cualquier request
    // autenticado de un usuario con este flag en true. Se cambia desde el
    // panel de super admin.
    public bool IsBanned { get; set; }

    public DateTime CreatedAt { get; set; }
 
    public ICollection<Project> OwnedProjects { get; set; } = new List<Project>();
    public ICollection<ProjectMember> ProjectMemberships { get; set; } = new List<ProjectMember>();
    public ICollection<TaskItem> CreatedTasks { get; set; } = new List<TaskItem>();
    public ICollection<TaskItem> AssignedTasks { get; set; } = new List<TaskItem>();
    public ICollection<TaskComment> Comments { get; set; } = new List<TaskComment>();
}
 