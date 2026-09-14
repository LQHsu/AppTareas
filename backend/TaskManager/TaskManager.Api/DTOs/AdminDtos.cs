namespace TaskManager.Api.DTOs;

public record UpdateSuperAdminDto(bool IsSuperAdmin);

public record UpdateBannedDto(bool IsBanned);

// Vista global de un proyecto para el panel de super admin. Se
// diferencia de ProjectDto en que agrega MemberCount y no filtra por
// membresia del solicitante (aqui se listan TODOS los proyectos).
public record AdminProjectDto(
    Guid Id,
    string Name,
    string? Description,
    int AreaId,
    string AreaNombre,
    string OwnerId,
    string OwnerFullName,
    bool IsArchived,
    DateTime CreatedAt,
    int MemberCount,
    int TotalTasks,
    int CompletedTasks
);
