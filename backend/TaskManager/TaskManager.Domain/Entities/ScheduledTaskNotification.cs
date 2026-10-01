namespace TaskManager.Domain.Entities;

// Aviso de "te asignaron esta tarea" que NO sale al instante sino a la
// hora que eligio quien asigno. Funciona como una cola: el controller
// inserta la fila al asignar, y ScheduledNotificationWorker la manda
// cuando SendAt ya paso. Pendiente = SentAt y CancelledAt nulos.
public class ScheduledTaskNotification
{
    public Guid Id { get; set; }

    public Guid TaskId { get; set; }
    public TaskItem Task { get; set; } = null!;

    // Quien debe recibir el aviso (la persona asignada al programarlo).
    // Sin propiedad de navegacion a User a proposito: solo se compara
    // contra Task.AssignedToId al enviar, y asi no se agrega otra FK.
    public string RecipientId { get; set; } = string.Empty;

    // UTC, igual que el resto de fechas del proyecto.
    public DateTime SendAt { get; set; }

    public DateTime CreatedAt { get; set; }

    // Fecha de envio (o de descarte, si la tarea ya no aplica). Nulo =
    // todavia pendiente.
    public DateTime? SentAt { get; set; }

    // Se llena si el aviso ya no tiene sentido antes de salir (la tarea
    // se reasigno, se quito la asignacion o se borro).
    public DateTime? CancelledAt { get; set; }
}
