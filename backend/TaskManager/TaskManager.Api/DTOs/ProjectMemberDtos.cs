namespace TaskManager.Api.DTOs;
 
public record ProjectMemberDto(
    string UserId,
    string FullName,
    string Email,
    DateTime JoinedAt,
    bool IsOwner
);

public record AddProjectMemberDto(string UserId);
 