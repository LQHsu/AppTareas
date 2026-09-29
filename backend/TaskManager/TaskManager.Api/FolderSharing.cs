using Microsoft.EntityFrameworkCore;
using TaskManager.Domain.Entities;
using TaskManager.Domain.Enums;
using TaskManager.Infrastructure;

namespace TaskManager.Api;

// Logica de "compartir una carpeta" (agregar como miembro + reasignar
// tareas), compartida por FoldersController.Share (accion manual del
// dueno) y UsersController.Create (asignacion automatica cuando la alta
// que activa el registro trae una oficina elegida de antemano - ver
// AltaCoordinador.OficinaId). No hace SaveChanges: el caller decide
// cuando persistir, para poder combinarlo con sus propios cambios.
public static class FolderSharing
{
    public static async Task<int> ShareAsync(AppDbContext db, ProjectFolder folder, User target, string actingUserId)
    {
        var projectIds = folder.Projects.Select(p => p.Id).ToList();

        // 1) Acceso: agregar como miembro donde no lo sea todavia.
        if (projectIds.Count > 0)
        {
            var yaMiembroEn = await db.ProjectMembers
                .Where(m => projectIds.Contains(m.ProjectId) && m.UserId == target.Id)
                .Select(m => m.ProjectId)
                .ToListAsync();

            foreach (var projectId in projectIds.Except(yaMiembroEn))
            {
                db.ProjectMembers.Add(new ProjectMember
                {
                    ProjectId = projectId,
                    UserId = target.Id,
                    JoinedAt = DateTime.UtcNow,
                });
            }
        }

        // 2) Reasignacion masiva. Incluye subtareas (tambien tienen
        // ProjectId) y tareas ya terminadas o canceladas: se pidio
        // explicitamente reasignar TODAS las de la carpeta.
        var ahora = DateTime.UtcNow;
        var tasks = await db.Tasks
            .Where(t => t.ProjectId.HasValue && projectIds.Contains(t.ProjectId.Value))
            .ToListAsync();

        var reasignadas = 0;

        foreach (var task in tasks)
        {
            if (task.AssignedToId == target.Id) continue;

            task.AssignedToId = target.Id;
            task.FechaAsignacion = ahora;
            task.UpdatedAt = ahora;
            reasignadas++;

            // Mismo comportamiento que PATCH /tasks/{id}/assign: una
            // tarea que seguia en Creada pasa a Asignada, con bitacora.
            if (task.Status == TaskItemStatus.Creada)
            {
                var oldStatus = task.Status;
                task.Status = TaskItemStatus.Asignada;

                db.TaskStatusHistories.Add(new TaskStatusHistory
                {
                    Id = Guid.NewGuid(),
                    TaskId = task.Id,
                    ChangedById = actingUserId,
                    OldStatus = oldStatus,
                    NewStatus = task.Status,
                    ChangedAt = ahora,
                });
            }
        }

        folder.SharedWithId = target.Id;
        folder.SharedAt = ahora;

        return reasignadas;
    }
}
