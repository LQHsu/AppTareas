namespace TaskManager.Api.DTOs;

public record AreaDto(int Id, string Nombre);

public record UserDto(
    Guid Id,
    string Username,
    string Email,
    string FullName,
    int AreaId,
    string AreaNombre,
    bool IsSuperAdmin,
    bool IsBanned
);

public record CreateUserDto(
    Guid Id, // viene del "sub" del token de Keycloak la primera vez que el usuario entra
    string Username,
    string Email,
    string FullName,
    int AreaId
);