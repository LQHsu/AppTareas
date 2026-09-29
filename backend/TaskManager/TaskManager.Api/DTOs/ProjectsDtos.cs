namespace TaskManager.Api.DTOs;

public record ProjectDto(
    Guid Id,
    string Name,
    string? Description,
    int AreaId,
    string AreaNombre,
    string OwnerId,
    string OwnerFullName,
    bool IsArchived,
    DateTime CreatedAt,
    int TotalTasks,
    int CompletedTasks,
    Guid? FolderId,
    string? FolderName,
    // Con quien esta compartida la carpeta del proyecto (null si no
    // tiene carpeta o la carpeta no esta compartida). El frontend la usa
    // para preseleccionar el asignado por defecto al crear una tarea.
    string? FolderSharedWithId,
    string? FolderSharedWithFullName,
    // Color propio del proyecto (hex), independiente del color de su
    // carpeta. FolderColor viene de Project.Folder.Color (null si no
    // tiene carpeta o la carpeta no tiene color) - el frontend lo usa
    // para el acento de "a que carpeta pertenezco".
    string? Color,
    string? FolderColor
);

// FolderId opcional: si viene, el proyecto nace ya dentro de esa
// carpeta (debe ser una carpeta propia del creador — se valida en el
// controller igual que en MoveToFolder).
public record CreateProjectDto(string Name, string? Description, Guid? FolderId = null, string? Color = null);

// Description viene como HTML (lo produce app-editor-texto).
public record UpdateProjectDto(string Name, string? Description, string? Color = null);