using System.Diagnostics;
using Worker.Models;

namespace Worker.Services;

public class ProjectManager
{
    private readonly string _workspacePath;
    private readonly ILogger<ProjectManager> _logger;

    public ProjectManager(IConfiguration configuration, ILogger<ProjectManager> logger)
    {
        _workspacePath = configuration["WorkspacePath"] ?? "/workspace";
        _logger = logger;
    }

    public async Task<List<Project>> GetProjectsAsync()
    {
        var projects = new List<Project>();

        if (!Directory.Exists(_workspacePath))
        {
            _logger.LogWarning("Workspace path does not exist: {Path}", _workspacePath);
            return projects;
        }

        var directories = Directory.GetDirectories(_workspacePath);

        foreach (var dir in directories)
        {
            var dirName = Path.GetFileName(dir);

            // Skip special directories
            if (dirName == "JobStatus" || dirName == "JobHistory" || dirName == "RemoteProgrammer")
                continue;

            if (await IsGitRepositoryAsync(dir))
            {
                var project = new Project
                {
                    Name = dirName,
                    Path = dir,
                    GitBranch = await GetCurrentBranchAsync(dir),
                    GitRemote = await GetRemoteUrlAsync(dir),
                    LastModified = Directory.GetLastWriteTime(dir)
                };

                projects.Add(project);
            }
        }

        // RemoteProgrammer 프로젝트도 포함
        var remoteProgrammerPath = Path.Combine(_workspacePath, "RemoteProgrammer");
        if (Directory.Exists(remoteProgrammerPath))
        {
            projects.Add(new Project
            {
                Name = "RemoteProgrammer",
                Path = remoteProgrammerPath,
                GitBranch = await GetCurrentBranchAsync(remoteProgrammerPath),
                GitRemote = await GetRemoteUrlAsync(remoteProgrammerPath),
                LastModified = Directory.GetLastWriteTime(remoteProgrammerPath)
            });
        }

        return projects;
    }

    public async Task<Project?> GetProjectByNameAsync(string projectName)
    {
        var projects = await GetProjectsAsync();
        return projects.FirstOrDefault(p => p.Name == projectName);
    }

    private async Task<bool> IsGitRepositoryAsync(string path)
    {
        var gitPath = Path.Combine(path, ".git");
        return await Task.FromResult(Directory.Exists(gitPath));
    }

    private async Task<string?> GetCurrentBranchAsync(string repoPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse --abbrev-ref HEAD",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current branch for {Path}", repoPath);
            return null;
        }
    }

    private async Task<string?> GetRemoteUrlAsync(string repoPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "remote get-url origin",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting remote URL for {Path}", repoPath);
            return null;
        }
    }
}
