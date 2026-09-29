namespace TaskManager.Domain.Enums;
 
public enum TaskItemStatus
{
    Creada = 0,
    Asignada = 1,
    Leida = 2,
    EnAtencion = 3,
    Atendida = 4,
    VolverARevisar = 5,
    Terminada = 6,
    Cancelada = 7,

    // Agregado despues (ver v2 2026-09-29): valor al final para no
    // renumerar los que ya existen (quedarian mal los TaskStatusHistory
    // ya guardados). Quien la tiene asignada, o el creador, la pueden
    // poner en pausa y regresarla a EnAtencion despues - mismo grupo de
    // permisos que EnAtencion/Atendida/VolverARevisar en
    // TasksController.UpdateStatus.
    Pausada = 8
}