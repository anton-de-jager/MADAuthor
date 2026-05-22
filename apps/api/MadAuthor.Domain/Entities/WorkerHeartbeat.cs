namespace MadAuthor.Domain.Entities;

public class WorkerHeartbeat
{
    public Guid Id { get; set; }
    public string WorkerId { get; set; } = string.Empty;
    public DateTime LastPing { get; set; }
    public Guid? LastJobId { get; set; }
}
