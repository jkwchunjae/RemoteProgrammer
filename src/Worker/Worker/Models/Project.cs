namespace Worker.Models;

public class Project
{
    public required string Name { get; set; }
    public required string Path { get; set; }
    public string? GitBranch { get; set; }
    public string? GitRemote { get; set; }
    public DateTime LastModified { get; set; }
}
