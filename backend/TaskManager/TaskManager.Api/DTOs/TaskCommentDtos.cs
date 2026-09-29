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

// Payload minimo para el evento SignalR "CommentDeleted" - a diferencia
// de "CommentAdded" (que manda el TaskCommentDto completo), aca no hace
// falta mas que el id para que el frontend lo quite de su lista en vivo.
public record CommentDeletedDto(Guid TaskId, Guid CommentId);
