using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TaskManager.Infrastructure;

namespace TaskManager.Api.Hubs;

// Hub de "solo escucha": los clientes nunca mandan cambios de datos por
// aca (eso sigue siendo HTTP normal via TasksController, con toda su
// validacion de negocio). Este hub unicamente:
//   - une la conexion al grupo personal del usuario al conectar, y
//   - expone JoinProject/LeaveProject para que proyecto-detalle reciba
//     los cambios de las tareas de ESE proyecto en particular.
// TasksController transmite el evento "TaskChanged" a estos grupos
// despues de cada mutacion exitosa (ver NotificarCambio ahi).
[Authorize]
public class TaskHub : Hub
{
    private readonly AppDbContext _db;

    public TaskHub(AppDbContext db)
    {
        _db = db;
    }

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, TaskHubGroups.User(GetUserId()));
        await base.OnConnectedAsync();
    }

    // Se valida membresia/propiedad igual que en TasksController.GetByProject:
    // sin esto, cualquiera podria invocar JoinProject con un id ajeno y
    // recibir titulo/descripcion de tareas de un proyecto en el que no
    // participa (el join por si solo no pasa por [Authorize] de accion,
    // a diferencia de un endpoint HTTP normal).
    public async Task JoinProject(Guid projectId)
    {
        var userId = GetUserId();

        var isMember = await _db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == userId);
        var isOwner = await _db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == userId);

        if (!isMember && !isOwner) return;

        await Groups.AddToGroupAsync(Context.ConnectionId, TaskHubGroups.Project(projectId));
    }

    public async Task LeaveProject(Guid projectId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, TaskHubGroups.Project(projectId));
    }

    private Guid GetUserId()
    {
        var sub = Context.User?.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("Token sin claim 'sub'.");

        return Guid.Parse(sub);
    }
}
