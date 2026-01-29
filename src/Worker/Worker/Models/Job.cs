using System.Text.Json.Serialization;

namespace Worker.Models;

public class Job
{
    public required string Id { get; set; }
    public required string ProjectName { get; set; }
    public required string ProjectPath { get; set; }
    public required string Description { get; set; }
    public required string BigTaskName { get; set; }
    public string? WorktreePath { get; set; }
    public JobStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> Logs { get; set; } = new();
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum JobStatus
{
    Pending,
    Running,
    WaitingForUserInput,
    Completed,
    Failed
}
