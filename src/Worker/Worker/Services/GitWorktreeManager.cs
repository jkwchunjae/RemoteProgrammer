using System.Diagnostics;
using System.Text.RegularExpressions;
using Worker.Models;
using Worker.Utils;

namespace Worker.Services;

public class GitWorktreeManager
{
    private readonly string _workspacePath;
    private readonly string _worktreesPath;
    private readonly string _metadataPath;
    private readonly ILogger<GitWorktreeManager> _logger;
    private readonly ISerializer _serializer;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, Worktree> _worktreeCache = new();

    public GitWorktreeManager(IConfiguration configuration, ILogger<GitWorktreeManager> logger, ISerializer serializer)
    {
        var configuredPath = configuration["WorkspacePath"] ?? "/workspace";
        _workspacePath = Path.GetFullPath(configuredPath);
        _worktreesPath = Path.Combine(_workspacePath, ".worktrees");
        _metadataPath = Path.Combine(_worktreesPath, ".metadata");
        _logger = logger;
        _serializer = serializer;

        Directory.CreateDirectory(_worktreesPath);
        Directory.CreateDirectory(_metadataPath);

        _logger.LogInformation("GitWorktreeManager initialized - Worktrees: {WorktreesPath}", _worktreesPath);

        LoadWorktreeMetadata();
    }

    public async Task<Worktree> GetOrCreateWorktreeAsync(string projectName, string projectPath, string branchName)
    {
        await _lock.WaitAsync();
        try
        {
            // 브랜치 이름 검증
            if (!IsValidBranchName(branchName))
            {
                throw new ArgumentException($"Invalid branch name: {branchName}");
            }

            var key = GetWorktreeKey(projectName, branchName);

            // 캐시에서 확인
            if (_worktreeCache.TryGetValue(key, out var cachedWorktree))
            {
                // InUse 상태면 에러
                if (cachedWorktree.Status == WorktreeStatus.InUse)
                {
                    throw new InvalidOperationException($"Branch '{branchName}' is currently in use. Please wait.");
                }

                // Active 상태면 LastUsedAt 업데이트
                cachedWorktree.LastUsedAt = DateTime.UtcNow;
                cachedWorktree.JobCount++;
                await SaveWorktreeMetadataAsync(cachedWorktree);

                _logger.LogInformation("Reusing existing worktree for {ProjectName}/{BranchName}", projectName, branchName);
                return cachedWorktree;
            }

            // 새 worktree 생성
            var worktreePath = Path.Combine(_worktreesPath, projectName, branchName);

            // worktree 경로가 이미 존재하는지 확인
            if (Directory.Exists(worktreePath))
            {
                _logger.LogInformation("Worktree directory exists, loading from disk: {WorktreePath}", worktreePath);
                // 메타데이터만 없는 경우 재생성
                var worktree = new Worktree
                {
                    ProjectName = projectName,
                    BranchName = branchName,
                    WorktreePath = worktreePath,
                    SourcePath = projectPath,
                    CreatedAt = DateTime.UtcNow,
                    LastUsedAt = DateTime.UtcNow,
                    JobCount = 1,
                    Status = WorktreeStatus.Active
                };

                _worktreeCache[key] = worktree;
                await SaveWorktreeMetadataAsync(worktree);
                return worktree;
            }
            else
            {
                // Git worktree 생성
                await CreateGitWorktreeAsync(projectPath, worktreePath, branchName);

                // Worktree 객체 생성
                var newWorktree = new Worktree
                {
                    ProjectName = projectName,
                    BranchName = branchName,
                    WorktreePath = worktreePath,
                    SourcePath = projectPath,
                    CreatedAt = DateTime.UtcNow,
                    LastUsedAt = DateTime.UtcNow,
                    JobCount = 1,
                    Status = WorktreeStatus.Active
                };

                _worktreeCache[key] = newWorktree;
                await SaveWorktreeMetadataAsync(newWorktree);

                _logger.LogInformation("Created new worktree for {ProjectName}/{BranchName} at {WorktreePath}",
                    projectName, branchName, worktreePath);

                return newWorktree;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetWorktreeStatusAsync(string projectName, string branchName, WorktreeStatus status)
    {
        await _lock.WaitAsync();
        try
        {
            var key = GetWorktreeKey(projectName, branchName);

            if (_worktreeCache.TryGetValue(key, out var worktree))
            {
                worktree.Status = status;
                await SaveWorktreeMetadataAsync(worktree);
                _logger.LogInformation("Updated worktree status for {ProjectName}/{BranchName} to {Status}",
                    projectName, branchName, status);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> RemoveWorktreeAsync(string projectName, string branchName)
    {
        await _lock.WaitAsync();
        try
        {
            var key = GetWorktreeKey(projectName, branchName);

            if (!_worktreeCache.TryGetValue(key, out var worktree))
            {
                _logger.LogWarning("Worktree not found: {ProjectName}/{BranchName}", projectName, branchName);
                return false;
            }

            // InUse 상태면 삭제 불가
            if (worktree.Status == WorktreeStatus.InUse)
            {
                throw new InvalidOperationException($"Cannot remove worktree '{branchName}' while in use.");
            }

            // Git worktree 제거
            await RemoveGitWorktreeAsync(worktree.SourcePath, worktree.WorktreePath);

            // 캐시에서 제거
            _worktreeCache.Remove(key);

            // 메타데이터 파일 삭제
            var metadataFile = GetMetadataFilePath(projectName, branchName);
            if (File.Exists(metadataFile))
            {
                File.Delete(metadataFile);
            }

            _logger.LogInformation("Removed worktree for {ProjectName}/{BranchName}", projectName, branchName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing worktree {ProjectName}/{BranchName}", projectName, branchName);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<Worktree>> GetAllWorktreesAsync(string? projectName = null)
    {
        await _lock.WaitAsync();
        try
        {
            var worktrees = _worktreeCache.Values.ToList();

            if (!string.IsNullOrEmpty(projectName))
            {
                worktrees = worktrees.Where(w => w.ProjectName == projectName).ToList();
            }

            return worktrees;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task CreateGitWorktreeAsync(string sourcePath, string worktreePath, string branchName)
    {
        try
        {
            // 디렉토리 생성
            Directory.CreateDirectory(Path.GetDirectoryName(worktreePath)!);

            // 브랜치가 이미 존재하는지 확인
            var branchExists = await CheckBranchExistsAsync(sourcePath, branchName);

            var arguments = branchExists
                ? $"worktree add \"{worktreePath}\" {branchName}"
                : $"worktree add -b {branchName} \"{worktreePath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = sourcePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                throw new Exception("Failed to start git process");
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new Exception($"Git worktree add failed: {error}");
            }

            _logger.LogInformation("Git worktree created: {Output}", output);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating git worktree at {WorktreePath}", worktreePath);
            throw;
        }
    }

    private async Task<bool> CheckBranchExistsAsync(string sourcePath, string branchName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"rev-parse --verify {branchName}",
                WorkingDirectory = sourcePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task RemoveGitWorktreeAsync(string sourcePath, string worktreePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"worktree remove \"{worktreePath}\" --force",
                WorkingDirectory = sourcePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                throw new Exception("Failed to start git process");
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("Git worktree remove warning: {Error}", error);
            }

            // 디렉토리가 남아있으면 강제 삭제
            if (Directory.Exists(worktreePath))
            {
                Directory.Delete(worktreePath, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing git worktree at {WorktreePath}", worktreePath);
            throw;
        }
    }

    private void LoadWorktreeMetadata()
    {
        if (!Directory.Exists(_metadataPath))
            return;

        var metadataFiles = Directory.GetFiles(_metadataPath, "*.json");

        foreach (var file in metadataFiles)
        {
            try
            {
                var json = File.ReadAllText(file);
                var worktree = _serializer.Deserialize<Worktree>(json);
                if (worktree != null)
                {
                    var key = GetWorktreeKey(worktree.ProjectName, worktree.BranchName);
                    _worktreeCache[key] = worktree;
                    _logger.LogInformation("Loaded worktree metadata: {ProjectName}/{BranchName}",
                        worktree.ProjectName, worktree.BranchName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading worktree metadata from {File}", file);
            }
        }
    }

    private async Task SaveWorktreeMetadataAsync(Worktree worktree)
    {
        try
        {
            var filePath = GetMetadataFilePath(worktree.ProjectName, worktree.BranchName);
            var json = _serializer.Serialize(worktree);
            await File.WriteAllTextAsync(filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving worktree metadata for {ProjectName}/{BranchName}",
                worktree.ProjectName, worktree.BranchName);
        }
    }

    private string GetMetadataFilePath(string projectName, string branchName)
    {
        var sanitizedBranch = branchName.Replace("/", "_").Replace("\\", "_");
        return Path.Combine(_metadataPath, $"{projectName}_{sanitizedBranch}.json");
    }

    private static string GetWorktreeKey(string projectName, string branchName)
    {
        return $"{projectName}::{branchName}";
    }

    private static bool IsValidBranchName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            return false;

        if (!Regex.IsMatch(name, @"^[a-zA-Z0-9가-힣/_-]+$"))
            return false;

        if (name.Contains("..") || name.Contains("@{") ||
            name.StartsWith(".") || name.EndsWith(".") ||
            name.EndsWith(".lock"))
            return false;

        return true;
    }
}
