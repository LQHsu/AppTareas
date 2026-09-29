using TaskManager.Domain.Entities;

namespace TaskManager.Api.Notificaciones;

// Punto unico para avisarle a alguien de un evento de una tarea, por
// los dos canales que existen (correo + Chat) a la vez - asi
// TasksController/TaskCommentsController no repiten la logica de "que
// mandar por cada canal" en cada endpoint que dispare un aviso.
//
// Best-effort en los dos canales: si el correo o el Chat fallan (relay
// caido, la persona nunca vinculo Chat, etc.), se loguea y se sigue -
// esto NUNCA debe tronar la operacion real (crear/asignar/cambiar
// estado) que disparo el aviso. Mismo criterio que ya usan
// EmailService/GoogleChatClient con su propio "Enabled".
public class TaskNotificationService
{
    private readonly EmailService _email;
    private readonly GoogleChatClient _chat;
    private readonly ILogger<TaskNotificationService> _logger;

    public TaskNotificationService(EmailService email, GoogleChatClient chat, ILogger<TaskNotificationService> logger)
    {
        _email = email;
        _chat = chat;
        _logger = logger;
    }

    // "Te asignaron esta tarea" - se llama desde TasksController.Create
    // (si nace ya asignada) y UpdateAssignee (si cambia el asignado).
    public async Task NotifyTaskAssignedAsync(TaskItem task, User assignee)
    {
        var taskUrl = $"https://apptareas.xoc.uam.mx/mis-tareas?tarea={task.Id}";

        if (_email.Enabled && assignee.NotifyByEmail)
        {
            try
            {
                var tituloEscapado = System.Net.WebUtility.HtmlEncode(task.Title);
                await _email.SendAsync(
                    assignee.Email,
                    assignee.FullName,
                    $"Se te asignó una tarea: {task.Title}",
                    $"<p>Hola {System.Net.WebUtility.HtmlEncode(assignee.FullName)},</p>" +
                    $"<p>Se te asignó la tarea <strong>{tituloEscapado}</strong>.</p>" +
                    $"<p><a href=\"{taskUrl}\">Ver la tarea en AppTareas</a></p>"
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "No se pudo mandar el correo de asignacion de la tarea {TaskId} a {Email}",
                    task.Id, assignee.Email);
            }
        }

        if (_chat.Enabled && assignee.NotifyByChat && !string.IsNullOrEmpty(assignee.GoogleChatUserId))
        {
            try
            {
                var space = await _chat.FindDmAsync(assignee.GoogleChatUserId);

                var nombreProyecto = task.Project?.Name ?? "AppTareas";
                await _chat.SendTaskCardAsync(
                    space,
                    task.Id,
                    $"Proyecto: {nombreProyecto}",

                    // https://developers.google.com/workspace/chat/format-messages#card_text_formatting
                    $"Se te asignó la tarea «<b>{System.Net.WebUtility.HtmlEncode(task.Title)}</b>».",

                    // El boton "Marcar como Atendida" todavia no es
                    // interactivo bajo el runtime de Add-ons (ver

                    incluirBotonAtendida: false,
                    assignedUserId: assignee.Id
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "No se pudo mandar la notificacion de Chat de la tarea {TaskId} a {UserId}",
                    task.Id, assignee.Id);
            }
        }
    }
}
