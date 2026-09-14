namespace TaskManager.Api.DTOs;

public record TaskAttachmentDto(
    Guid Id,
    Guid TaskId,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string UploadedById,
    string UploadedByFullName,
    DateTime UploadedAt
);

// Vista de un adjunto a nivel proyecto: agrega el titulo de la tarea a
// la que pertenece, para poder listar en una sola tabla los archivos de
// todas las tareas del proyecto. La descarga reusa el endpoint por
// tarea (de ahi que se incluya TaskId).
public record ProjectAttachmentDto(
    Guid Id,
    Guid TaskId,
    string TaskTitle,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string UploadedById,
    string UploadedByFullName,
    DateTime UploadedAt
);
