namespace Worker.Models;

public class WorkerMessage
{
    public required string Type { get; set; }
    public object? Data { get; set; }
}

public class JobRequest
{
    public required string JobId { get; set; }
    public required string ProjectName { get; set; }
    public required string BigTaskName { get; set; }
    public required string Description { get; set; }
}

public class JobResponse
{
    public required string JobId { get; set; }
    public required string Status { get; set; }
    public string? Result { get; set; }
    public string? ErrorMessage { get; set; }
}

public class UserInputRequest
{
    public required string JobId { get; set; }
    public required string Question { get; set; }
}

public class UserInputResponse
{
    public required string JobId { get; set; }
    public required string Answer { get; set; }
}

public class WorkerStatus
{
    public List<string> Projects { get; set; } = new();
    public List<JobSummary> RunningJobs { get; set; } = new();
}

public class JobSummary
{
    public required string JobId { get; set; }
    public required string ProjectName { get; set; }
    public required string Status { get; set; }
}
