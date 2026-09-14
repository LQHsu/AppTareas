namespace TaskManager.Api.Hubs;

// Nombres de grupo de SignalR centralizados: los usa tanto TaskHub (para
// unir/quitar conexiones) como TasksController (para transmitir los
// cambios) — un solo lugar para el formato, evita que se desincronicen
// por un typo en el prefijo.
public static class TaskHubGroups
{
    // Cualquiera viendo el tablero de un proyecto (proyecto-detalle) se
    // une aca: recibe cualquier cambio de cualquier tarea de ese proyecto.
    public static string Project(Guid projectId) => $"project:{projectId}";

    // Todo usuario se une a su propio grupo automaticamente al conectar
    // (ver TaskHub.OnConnectedAsync). Cubre "Mis tareas" -que mezcla
    // tareas de varios proyectos y tareas sueltas- sin tener que unirse
    // a un grupo por cada proyecto donde tenga algo asignado.
    public static string User(Guid userId) => $"user:{userId}";
}
