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

    // Rol de coordinador: puede dar de alta numeros economicos en su
    // propia area (ver AltaCoordinador/CoordinadorController) para que
    // esas personas puedan entrar a la app. No implica IsSuperAdmin ni
    // viceversa - un super admin que tambien quiera dar altas necesita
    // este flag aparte. Mismo criterio que IsSuperAdmin: columna propia,
    // se otorga desde el panel de super admin, nada de roles de Keycloak.
    public bool IsCoordinador { get; set; }

    // Baneo a nivel app: Keycloak sigue emitiendo un JWT valido para este
    // usuario (no hay forma de revocar eso desde aca), asi que el bloqueo
    // real ocurre en BanCheckMiddleware, que corta cualquier request
    // autenticado de un usuario con este flag en true. Se cambia desde el
    // panel de super admin.
    public bool IsBanned { get; set; }

    public DateTime CreatedAt { get; set; }

    // ID de Directory de Google Chat ("users/{id}", ver User.Name en el
    // evento que manda Chat), NO el correo - App Authentication (el
    // service account propio de la app, ver GoogleChatClient) no acepta
    // buscar un DM por email, solo por este id. Se llena la primera vez
    // que la persona le escribe al bot y confirma su correo
    // institucional (ver ChatInteractionsController - flujo de
    // vinculacion). Null = todavia no vinculo Chat, no puede recibir
    // notificaciones por ese canal (el resto de la app sigue
    // funcionando igual).
    public string? GoogleChatUserId { get; set; }

    // Preferencias de notificacion por canal (ver TaskNotificationService),
    // independientes entre si - alguien puede querer solo Chat, solo
    // correo, ambos o ninguno. true por default para que quien nunca
    // toco el menu siga recibiendo avisos igual que antes de este
    // campo. Se editan desde el propio usuario (PATCH /api/users/me/notifications),
    // no hay pantalla de admin para esto.
    public bool NotifyByEmail { get; set; } = true;
    public bool NotifyByChat { get; set; } = true;

    public ICollection<Project> OwnedProjects { get; set; } = new List<Project>();
    public ICollection<ProjectMember> ProjectMemberships { get; set; } = new List<ProjectMember>();
    public ICollection<TaskItem> CreatedTasks { get; set; } = new List<TaskItem>();
    public ICollection<TaskItem> AssignedTasks { get; set; } = new List<TaskItem>();
    public ICollection<TaskComment> Comments { get; set; } = new List<TaskComment>();
}
 