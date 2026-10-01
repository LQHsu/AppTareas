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
    public Task NotifyTaskAssignedAsync(TaskItem task, User assignee) =>
        SendAsync(
            task,
            assignee,
            emailSubject: $"Se te asignó una tarea: {task.Title}",
            emailBodyLead: $"<p>Se te asignó la tarea <strong>{{0}}</strong>.</p>",
            chatText: "Se te asignó la tarea «<b>{0}</b>»."
        );

    // "Se reanudo una tarea que tenias pausada" - se llama desde
    // TasksController.UpdateStatus cuando el creador (unico que puede
    // hacerlo mientras esta Pausada, ver el switch ahi) la mueve a
    // cualquier otro estado. Solo tiene sentido si sigue habiendo un
    // asignado (ver el chequeo antes de llamar este metodo).
    public Task NotifyTaskResumedAsync(TaskItem task, User assignee) =>
        SendAsync(
            task,
            assignee,
            emailSubject: $"Se reanudó una tarea: {task.Title}",
            emailBodyLead: $"<p>La tarea <strong>{{0}}</strong>, que estaba pausada, se reanudó.</p>",
            chatText: "La tarea «<b>{0}</b>», que estaba pausada, se reanudó."
        );

    // "La tarea que te asignaron necesita que la corrijas" - se llama
    // desde TasksController.UpdateStatus cuando pasa a VolverARevisar.
    // Aviso inverso a los de PersonasImportantes de abajo: aca quien
    // necesita enterarse es la persona asignada, no quien pidio la
    // revision (por eso el caller ya excluye el caso de que sean la
    // misma persona, ver el chequeo antes de llamar este metodo).
    public Task NotifyTaskNeedsReviewAsync(TaskItem task, User assignee) =>
        SendAsync(
            task,
            assignee,
            emailSubject: $"Una tarea necesita corregirse: {task.Title}",
            emailBodyLead: $"<p>La tarea <strong>{{0}}</strong> se marcó como \"Volver a revisar\": hay que corregirla.</p>",
            chatText: "La tarea «<b>{0}</b>» se marcó como \"Volver a revisar\": hay que corregirla."
        );

    // "Alguien leyo/atendio una tarea que administras" - a diferencia de
    // los avisos de arriba (un solo destinatario fijo, la persona
    // asignada), estos van a las "personas importantes" de la tarea:
    // quien la creo, el dueno del proyecto y el dueno de la oficina
    // ("carpeta") en la que vive el proyecto. Frecuentemente son la
    // misma persona en mas de un rol (alguien que crea sus propias
    // tareas en su propio proyecto, o que es dueno de la oficina y del
    // proyecto a la vez) - PersonasImportantes deduplica por Id para que
    // reciba UN solo correo, no uno por rol.
    public async Task NotifyTaskReadAsync(TaskItem task, string actorUserId)
    {
        foreach (var persona in PersonasImportantes(task, actorUserId))
        {
            await SendAsync(
                task,
                persona,
                emailSubject: $"Se leyó una tarea: {task.Title}",
                emailBodyLead: "<p>La persona asignada abrió la tarea <strong>{0}</strong>.</p>",
                chatText: "Se leyó la tarea «<b>{0}</b>»."
            );
        }
    }

    public async Task NotifyTaskAttendedAsync(TaskItem task, string actorUserId)
    {
        foreach (var persona in PersonasImportantes(task, actorUserId))
        {
            await SendAsync(
                task,
                persona,
                emailSubject: $"Se atendió una tarea: {task.Title}",
                emailBodyLead: "<p>La tarea <strong>{0}</strong> se marcó como atendida.</p>",
                chatText: "La tarea «<b>{0}</b>» se marcó como atendida."
            );
        }
    }

    // "Alguien comento una tarea" - a diferencia de Leida/Atendida (que
    // siempre las dispara quien tiene la tarea asignada) o VolverARevisar
    // (que siempre la dispara "el otro lado"), un comentario lo puede
    // escribir cualquiera de los involucrados - por eso el destinatario
    // es PersonasRelacionadas completo (incluye tambien al asignado) en
    // vez de PersonasImportantes, siempre excluyendo a quien comento.
    public async Task NotifyCommentAddedAsync(TaskItem task, string commentContent, string actorUserId)
    {
        var contenidoEscapado = System.Net.WebUtility.HtmlEncode(commentContent);

        foreach (var persona in PersonasRelacionadas(task, actorUserId))
        {
            await SendAsync(
                task,
                persona,
                emailSubject: $"Nuevo comentario en una tarea: {task.Title}",
                emailBodyLead: $"<p>Se comentó en la tarea <strong>{{0}}</strong>:</p><blockquote>{contenidoEscapado}</blockquote>",
                chatText: $"Se comentó en la tarea «<b>{{0}}</b>»: {contenidoEscapado}"
            );
        }
    }

    // Creador de la tarea + dueno del proyecto (si tiene) + dueno de la
    // oficina/carpeta del proyecto (si tiene), sin duplicados y sin
    // incluir a quien disparo la accion (actorUserId) - avisarle de algo
    // que el mismo acaba de hacer no aporta nada. HashSet por Id, no por
    // referencia: son tres consultas EF distintas (CreatedBy, Project.
    // Owner, Project.Folder.Owner), pueden ser tres instancias de User
    // aunque representen a la misma persona.
    private static IEnumerable<User> PersonasImportantes(TaskItem task, string actorUserId)
    {
        var vistos = new HashSet<string> { actorUserId };

        foreach (var persona in new[] { task.CreatedBy, task.Project?.Owner, task.Project?.Folder?.Owner })
        {
            if (persona is not null && vistos.Add(persona.Id))
            {
                yield return persona;
            }
        }
    }

    // Igual que PersonasImportantes, sumando tambien a quien tiene la
    // tarea asignada - un comentario le interesa a TODOS los
    // involucrados, no solo al lado "administrativo".
    private static IEnumerable<User> PersonasRelacionadas(TaskItem task, string actorUserId)
    {
        var vistos = new HashSet<string> { actorUserId };

        foreach (var persona in new[] { task.CreatedBy, task.Project?.Owner, task.Project?.Folder?.Owner, task.AssignedTo })
        {
            if (persona is not null && vistos.Add(persona.Id))
            {
                yield return persona;
            }
        }
    }

    // Comun a todos los avisos de arriba: mismo par de canales (correo +
    // Chat), mismo criterio best-effort, solo cambia el texto. emailBodyLead
    // y chatText traen un "{0}" para el titulo ya escapado, de forma que
    // cada llamada no tenga que repetir el HtmlEncode a mano.
    private async Task SendAsync(
        TaskItem task,
        User destinatario,
        string emailSubject,
        string emailBodyLead,
        string chatText)
    {
        var taskUrl = $"https://apptareas.xoc.uam.mx/mis-tareas?tarea={task.Id}";
        var tituloEscapado = System.Net.WebUtility.HtmlEncode(task.Title);

        if (_email.Enabled && destinatario.NotifyByEmail)
        {
            try
            {
                await _email.SendAsync(
                    destinatario.Email,
                    destinatario.FullName,
                    emailSubject,
                    $"<p>Hola {System.Net.WebUtility.HtmlEncode(destinatario.FullName)},</p>" +
                    string.Format(emailBodyLead, tituloEscapado) +
                    $"<p><a href=\"{taskUrl}\">Ver la tarea en AppTareas</a></p>"
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "No se pudo mandar el correo '{Subject}' de la tarea {TaskId} a {Email}",
                    emailSubject, task.Id, destinatario.Email);
            }
        }

        if (_chat.Enabled && destinatario.NotifyByChat && !string.IsNullOrEmpty(destinatario.GoogleChatUserId))
        {
            try
            {
                var space = await _chat.FindDmAsync(destinatario.GoogleChatUserId);

                var nombreProyecto = System.Net.WebUtility.HtmlEncode(task.Project?.Name) ?? "AppTareas";
                await _chat.SendTaskCardAsync(
                    space,
                    task.Id,
                   
                    $"Proyecto: {nombreProyecto}",

                    // https://developers.google.com/workspace/chat/format-messages#card_text_formatting
                    string.Format(chatText, tituloEscapado),

                    incluirBotonAtendida: false,
                    assignedUserId: destinatario.Id
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "No se pudo mandar la notificacion de Chat '{Subject}' de la tarea {TaskId} a {UserId}",
                    emailSubject, task.Id, destinatario.Id);
            }
        }
    }
}
