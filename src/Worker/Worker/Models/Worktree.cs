namespace Worker.Models;

public class Worktree
{
    public required string ProjectName { get; set; }
    public required string BranchName { get; set; }
    public required string WorktreePath { get; set; }
    public required string SourcePath { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastUsedAt { get; set; }
    public int JobCount { get; set; }
    public WorktreeStatus Status { get; set; }
}

public enum WorktreeStatus
{
    Active,     // 사용 가능
    InUse       // 작업 실행 중 (동시 실행 방지용)
}
