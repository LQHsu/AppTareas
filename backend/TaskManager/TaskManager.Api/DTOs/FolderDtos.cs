namespace TaskManager.Api.DTOs;

// ProjectCount/TaskCount se usan en el frontend para avisar cuantas
// tareas se van a reasignar antes de confirmar el compartir.
public record FolderDto(
    Guid Id,
    string Name,
    Guid OwnerId,
    string OwnerFullName,
    Guid? SharedWithId,
    string? SharedWithFullName,
    DateTime? SharedAt,
    DateTime CreatedAt,
    int ProjectCount,
    int TaskCount,
    // true si el usuario que consulta es el dueno (puede administrarla);
    // false si solo la ve porque se la compartieron.
    bool IsOwner
);

public record CreateFolderDto(string Name);

public record UpdateFolderDto(string Name);

// UserId null = dejar de compartir (no reasigna nada, ver ShareFolder).
public record ShareFolderDto(Guid? UserId);

// Resultado de compartir: cuantas tareas se reasignaron de verdad.
public record ShareFolderResultDto(FolderDto Folder, int ReassignedTasks);

// FolderId null = sacar el proyecto de cualquier carpeta.
public record MoveProjectToFolderDto(Guid? FolderId);
