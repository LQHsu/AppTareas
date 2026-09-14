namespace TaskManager.Api.DTOs;
 
public record ProjectMemberDto(
    Guid UserId,
    string FullName,
    string Email,
    DateTime JoinedAt,
    bool IsOwner
);
 
public record AddProjectMemberDto(Guid UserId);
 