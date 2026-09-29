namespace TaskManager.Domain.Entities;

// Lista blanca de numeros economicos autorizados a entrar a la app,
// cargada por un coordinador (User.IsCoordinador) antes de que la
// persona inicie sesion siquiera. Reemplaza el auto-registro abierto:
// UsersController.Create ya no crea un User para cualquier login valido
// de Keycloak, solo si su numero economico aparece aqui (ver comentario
// ahi). Es especifico del proceso de UAMX, no de la app en general.
public class AltaCoordinador
{
    // El numero economico "pelado" (ej. "48394"), NO el "sub" completo
    // de Keycloak ("f:<uuid-del-provider>:48394") - el coordinador solo
    // conoce el numero economico, nunca ese id compuesto. Ver
    // UsersController.ExtraerNumeroEconomico para como se extrae del
    // token al comparar contra esta tabla.
    public string NumeroEconomico { get; set; } = string.Empty;

    // A que area queda asignada la persona en cuanto activa su cuenta
    // (su primer login real). Es la del coordinador que la dio de alta,
    // no algo que la propia persona elige - evita que alguien se
    // autoasigne a un area distinta al completar su registro.
    public int AreaId { get; set; }
    public Area Area { get; set; } = null!;

    public string CreatedById { get; set; } = string.Empty;
    public User CreatedBy { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    // Foto de correo/area institucional (info_usuarios_unidad) tomada al
    // momento de dar la alta - no se vuelve a consultar despues, asi que
    // si esa BD cambia mas tarde (la persona cambia de adscripcion, etc.)
    // esto NO se actualiza solo. Es para mostrar en la tabla quien es
    // quien de un vistazo, no una fuente de verdad viva. Null si
    // info_usuarios_unidad no tenia dato (o no estaba configurado) en
    // ese momento.
    public string? CorreoInstitucional { get; set; }
    public string? AreaInstitucional { get; set; }

    // Foto del nombre completo (CUSXACDI, mismo origen que el preview -
    // ver CusxacdiInfoClient), tomada al momento de dar la alta por la
    // misma razon que CorreoInstitucional/AreaInstitucional arriba: es
    // solo para mostrar en la tabla, no se vuelve a consultar despues.
    // Null si CUSXACDI no tenia dato en ese momento.
    public string? NombreCompleto { get; set; }

    // Null mientras la persona no ha iniciado sesion todavia (alta
    // "pendiente"). Se llena la primera vez que su numero economico
    // completa el registro real en Users - no se borra el renglon
    // despues, queda como bitacora de quien la dio de alta y cuando.
    public DateTime? ActivatedAt { get; set; }

    // El User real creado al activarse (mismo momento que ActivatedAt).
    // Null mientras sigue pendiente. Se guarda para poder resolver la
    // oficina VIGENTE de esa persona (ver CoordinadorController.GetAltas)
    // sin tener que reconstruir el "sub" completo de Keycloak a partir
    // del numero economico (no es posible: el "sub" trae un uuid del
    // provider que este lado no conoce, ver ExtraerNumeroEconomico).
    public string? UserId { get; set; }
    public User? User { get; set; }

    // Oficina (ProjectFolder) que se le asigna a esta persona ANTES de
    // activarse. Opcional - null si no se elige ninguna. Al activarse
    // (ver UsersController.Create), se comparte esa oficina con el
    // nuevo usuario, con el mismo efecto que FoldersController.Share: se
    // le agrega a sus proyectos y se le reasignan todas sus tareas.
    // Null si la oficina se borro despues de dar esta alta (SetNull),
    // sin que eso invalide la alta.
    //
    // OJO: una vez activada, esto queda CONGELADO en la oficina que
    // tenia al momento de activarse - si despues se comparte una
    // carpeta distinta con esa persona (o se deja de compartir esta),
    // este campo no se actualiza solo. La oficina VIGENTE para alguien
    // ya activado se resuelve en vivo contra ProjectFolders (ver
    // GetAltas), no leyendo este campo directo.
    public Guid? OficinaId { get; set; }
    public ProjectFolder? Oficina { get; set; }
}
