namespace TaskManager.Api.DTOs;

public record ProjectDto(
    Guid Id,
    string Name,
    string? Description,
    int AreaId,
    string AreaNombre,
    Guid OwnerId,
    string OwnerFullName,
    bool IsArchived,
    DateTime CreatedAt,
    int TotalTasks,
    int CompletedTasks,
    Guid? FolderId,
    string? FolderName
);

// FolderId opcional: si viene, el proyecto nace ya dentro de esa
// carpeta (debe ser una carpeta propia del creador — se valida en el
// controller igual que en MoveToFolder).
public record CreateProjectDto(string Name, string? Description, Guid? FolderId = null);

// Description viene como HTML (lo produce app-editor-texto).
public record UpdateProjectDto(string Name, string? Description);