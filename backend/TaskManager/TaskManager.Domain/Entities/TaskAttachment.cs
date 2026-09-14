namespace TaskManager.Domain.Entities;
 
public class TaskAttachment
{
    public Guid Id { get; set; }
 
    public Guid TaskId { get; set; }
    public TaskItem Task { get; set; } = null!;
 
    public string FileName { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
 
    public Guid UploadedById { get; set; }
    public User UploadedBy { get; set; } = null!;
 
    public DateTime UploadedAt { get; set; }
}
 