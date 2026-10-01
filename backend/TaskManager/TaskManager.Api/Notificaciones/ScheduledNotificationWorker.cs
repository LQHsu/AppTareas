using Microsoft.EntityFrameworkCore;
using TaskManager.Infrastructure;

namespace TaskManager.Api.Notificaciones;

// Manda los avisos de asignacion que se programaron para una hora futura
// (ver ScheduledTaskNotification y TasksController.AvisarAsignacionAsync).
// Cada minuto busca los vencidos y los pasa por TaskNotificationService,
// el mismo camino que el aviso inmediato.
//
// Asume UNA sola instancia del servidor: no hay bloqueo entre procesos,
// con dos corriendo el mismo aviso podria salir duplicado.
//
// Es singleton y AppDbContext es scoped, por eso cada vuelta crea su
// propio scope. El estado vive en la base, asi que un reinicio no pierde
// avisos: los que vencieron durante la caida salen al arrancar.
public class ScheduledNotificationWorker : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly TaskNotificationService _notifications;
    private readonly ILogger<ScheduledNotificationWorker> _logger;

    public ScheduledNotificationWorker(
        IServiceScopeFactory scopes,
        TaskNotificationService notifications,
        ILogger<ScheduledNotificationWorker> logger)
    {
        _scopes = scopes;
        _notifications = notifications;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Intervalo);

        do
        {
            try
            {
                await EnviarVencidosAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Nunca dejar morir el worker por un fallo puntual (BD
                // caida un momento, etc.): se reintenta en la siguiente vuelta.
                _logger.LogError(ex, "Fallo al procesar los avisos programados");
            }
        }
        while (await WaitNextTickAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitNextTickAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    private async Task EnviarVencidosAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ahora = DateTime.UtcNow;

        var vencidos = await db.ScheduledTaskNotifications
            .Where(n => n.SentAt == null && n.CancelledAt == null && n.SendAt <= ahora)
            .OrderBy(n => n.SendAt)
            .ToListAsync(ct);

        foreach (var aviso in vencidos)
        {
            // _db.Tasks aplica el filtro de papelera: una tarea borrada
            // aparece como null y su aviso se cancela.
            var task = await db.Tasks
                .Include(t => t.Project)
                .ThenInclude(p => p!.Folder)
                .Include(t => t.CreatedBy)
                .Include(t => t.AssignedTo)
                .FirstOrDefaultAsync(t => t.Id == aviso.TaskId, ct);

            if (task?.AssignedTo is null || task.AssignedToId != aviso.RecipientId)
            {
                // Se borro, se quito la asignacion o se reasigno por fuera
                // del endpoint: el aviso ya no tiene destinatario valido.
                aviso.CancelledAt = DateTime.UtcNow;
            }
            else
            {
                // Best-effort: NotifyTaskAssignedAsync ya atrapa y loguea
                // los fallos de correo/Chat, asi que no hay reintento.
                await _notifications.NotifyTaskAssignedAsync(task, task.AssignedTo);
                aviso.SentAt = DateTime.UtcNow;
            }

            // Guardar por aviso: si el proceso cae a mitad, los ya
            // enviados quedan marcados y no se repiten.
            await db.SaveChangesAsync(ct);
        }
    }
}
