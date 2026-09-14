namespace TaskManager.Api.DTOs;

public record TaskCommentDto(
    Guid Id,
    Guid TaskId,
    string UserId,
    string UserFullName,
    string Content,
    DateTime CreatedAt
);

public record CreateTaskCommentDto(string Content);
