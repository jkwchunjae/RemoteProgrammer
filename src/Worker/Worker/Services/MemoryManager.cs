using System.Text;
using Worker.Models;

namespace Worker.Services;

/// <summary>
/// 큰작업(Big Task/Branch) 단위로 컨텍스트를 유지하기 위한 memory.md 파일 관리 서비스
/// </summary>
public class MemoryManager
{
    private readonly ILogger<MemoryManager> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private const string MemoryFileName = "memory.md";

    public MemoryManager(ILogger<MemoryManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// memory.md 파일이 존재하는지 확인
    /// </summary>
    public Task<bool> MemoryFileExistsAsync(string worktreePath)
    {
        var memoryPath = Path.Combine(worktreePath, MemoryFileName);
        return Task.FromResult(File.Exists(memoryPath));
    }

    /// <summary>
    /// memory.md 파일 초기화 (헤더 생성)
    /// </summary>
    public async Task InitializeMemoryFileAsync(Worktree worktree)
    {
        await _lock.WaitAsync();
        try
        {
            var memoryPath = Path.Combine(worktree.WorktreePath, MemoryFileName);

            // 이미 존재하면 스킵
            if (File.Exists(memoryPath))
            {
                _logger.LogInformation("Memory file already exists at {MemoryPath}", memoryPath);
                return;
            }

            var header = new StringBuilder();
            header.AppendLine($"# Big Task: {worktree.BranchName}");
            header.AppendLine($"**Project:** {worktree.ProjectName}");
            header.AppendLine($"**Started:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}");
            header.AppendLine($"**Last Updated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}");
            header.AppendLine();
            header.AppendLine("---");
            header.AppendLine();
            header.AppendLine("## Job History");
            header.AppendLine();

            await File.WriteAllTextAsync(memoryPath, header.ToString());

            _logger.LogInformation("Initialized memory.md at {MemoryPath}", memoryPath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to initialize memory.md at {WorktreePath}", worktree.WorktreePath);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied when initializing memory.md at {WorktreePath}", worktree.WorktreePath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error initializing memory.md at {WorktreePath}", worktree.WorktreePath);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 작업 결과를 memory.md에 추가
    /// </summary>
    public async Task AppendJobResultAsync(Job job, string worktreePath)
    {
        await _lock.WaitAsync();
        try
        {
            var memoryPath = Path.Combine(worktreePath, MemoryFileName);

            // memory.md가 없으면 로그만 남기고 스킵
            if (!File.Exists(memoryPath))
            {
                _logger.LogWarning("Memory file not found at {MemoryPath}. Skipping append.", memoryPath);
                return;
            }

            // 작업 결과 포맷팅
            var jobEntry = new StringBuilder();
            jobEntry.AppendLine($"### Job: {job.Id} ({DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC})");
            jobEntry.AppendLine($"**Status:** {job.Status}");
            jobEntry.AppendLine();
            jobEntry.AppendLine("**Description:**");
            jobEntry.AppendLine("```");
            jobEntry.AppendLine(job.Description);
            jobEntry.AppendLine("```");
            jobEntry.AppendLine();

            // 결과 또는 에러 메시지 추가
            if (job.Status == JobStatus.Completed && !string.IsNullOrWhiteSpace(job.Result))
            {
                jobEntry.AppendLine("**Result:**");
                jobEntry.AppendLine("```");
                // 결과가 너무 길면 잘라내기 (처음 2000자만 저장)
                var result = job.Result.Length > 2000
                    ? job.Result.Substring(0, 2000) + "\n... (truncated)"
                    : job.Result;
                jobEntry.AppendLine(result);
                jobEntry.AppendLine("```");
            }
            else if (job.Status == JobStatus.Failed && !string.IsNullOrWhiteSpace(job.ErrorMessage))
            {
                jobEntry.AppendLine("**Error:**");
                jobEntry.AppendLine("```");
                var error = job.ErrorMessage.Length > 2000
                    ? job.ErrorMessage.Substring(0, 2000) + "\n... (truncated)"
                    : job.ErrorMessage;
                jobEntry.AppendLine(error);
                jobEntry.AppendLine("```");
            }

            jobEntry.AppendLine();
            jobEntry.AppendLine("---");
            jobEntry.AppendLine();

            // 파일에 추가
            await File.AppendAllTextAsync(memoryPath, jobEntry.ToString());

            // 헤더의 "Last Updated" 업데이트
            await UpdateLastUpdatedTimestampAsync(memoryPath);

            _logger.LogInformation("Appended job {JobId} to memory.md at {MemoryPath}", job.Id, memoryPath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to append job {JobId} to memory.md at {WorktreePath}", job.Id, worktreePath);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied when appending to memory.md at {WorktreePath}", worktreePath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error appending job {JobId} to memory.md at {WorktreePath}", job.Id, worktreePath);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// memory.md 헤더의 "Last Updated" 타임스탬프 업데이트
    /// </summary>
    private async Task UpdateLastUpdatedTimestampAsync(string memoryPath)
    {
        try
        {
            var content = await File.ReadAllTextAsync(memoryPath);
            var lines = content.Split('\n').ToList();

            // "Last Updated" 라인 찾아서 업데이트
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].StartsWith("**Last Updated:**"))
                {
                    lines[i] = $"**Last Updated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}";
                    break;
                }
            }

            await File.WriteAllTextAsync(memoryPath, string.Join('\n', lines));
        }
        catch (Exception ex)
        {
            // 타임스탬프 업데이트 실패는 치명적이지 않으므로 로그만 남김
            _logger.LogWarning(ex, "Failed to update Last Updated timestamp in {MemoryPath}", memoryPath);
        }
    }
}
