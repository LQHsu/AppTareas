namespace TaskManager.Domain.Entities;
 
public class TaskComment
{
    public Guid Id { get; set; }
 
    public Guid TaskId { get; set; }
    public TaskItem Task { get; set; } = null!;
 
    public string UserId { get; set; } = string.Empty;
    public User User { get; set; } = null!;
 
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}