using System.Diagnostics;
using System.Text;
using Worker.Models;

namespace Worker.Services;

public class ClaudeCodeExecutor
{
    private readonly ILogger<ClaudeCodeExecutor> _logger;
    private readonly JobManager _jobManager;
    private readonly IClaudeExecutionStrategy _executionStrategy;
    private readonly GitWorktreeManager _worktreeManager;
    private readonly MemoryManager _memoryManager;

    public ClaudeCodeExecutor(
        ILogger<ClaudeCodeExecutor> logger,
        JobManager jobManager,
        IClaudeExecutionStrategy executionStrategy,
        GitWorktreeManager worktreeManager,
        MemoryManager memoryManager)
    {
        _logger = logger;
        _jobManager = jobManager;
        _executionStrategy = executionStrategy;
        _worktreeManager = worktreeManager;
        _memoryManager = memoryManager;
    }

    public async Task<(bool Success, string Output, string Error)> ExecuteJobAsync(Job job, CancellationToken cancellationToken = default)
    {
        string? scriptPath = null;
        string? workingPath = null;

        try
        {
            // 1. Worktree 가져오기/생성
            await _jobManager.AddJobLogAsync(job.Id, $"Getting or creating worktree for branch '{job.BigTaskName}'");
            var worktree = await _worktreeManager.GetOrCreateWorktreeAsync(
                job.ProjectName,
                job.ProjectPath,
                job.BigTaskName);

            // 2. Worktree 상태를 InUse로 변경 (동시 실행 방지)
            await _worktreeManager.SetWorktreeStatusAsync(
                job.ProjectName,
                job.BigTaskName,
                WorktreeStatus.InUse);

            // 3. Job에 worktree 경로 기록
            workingPath = worktree.WorktreePath;
            job.WorktreePath = workingPath;

            // 3.5. memory.md 초기화 (첫 작업인 경우)
            if (!await _memoryManager.MemoryFileExistsAsync(workingPath))
            {
                await _memoryManager.InitializeMemoryFileAsync(worktree);
                await _jobManager.AddJobLogAsync(job.Id, "Initialized memory.md for this branch");
            }

            // 4. Job 상태 Running으로 변경
            await _jobManager.UpdateJobStatusAsync(job.Id, JobStatus.Running);
            await _jobManager.AddJobLogAsync(job.Id, $"Starting execution in worktree: {workingPath}");

            // 5. OS별 전략을 사용하여 스크립트 생성 (worktree 경로 사용)
            scriptPath = await _executionStrategy.CreateExecutionScriptAsync(job, workingPath);

            // 6. OS별 전략을 사용하여 ProcessStartInfo 생성 (worktree 경로를 WorkingDirectory로)
            var psi = _executionStrategy.GetProcessStartInfo(scriptPath, workingPath);

            // 7. 실행
            using var process = new Process { StartInfo = psi };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                {
                    outputBuilder.AppendLine(args.Data);
                    _logger.LogInformation("[{JobId}] {Output}", job.Id, args.Data);
                    _jobManager.AddJobLogAsync(job.Id, args.Data).Wait();
                }
            };

            process.ErrorDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                {
                    errorBuilder.AppendLine(args.Data);
                    _logger.LogWarning("[{JobId}] {Error}", job.Id, args.Data);
                    _jobManager.AddJobLogAsync(job.Id, $"ERROR: {args.Data}").Wait();
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();
            var success = process.ExitCode == 0;

            if (success)
            {
                await _jobManager.UpdateJobStatusAsync(job.Id, JobStatus.Completed, output);
                await _jobManager.AddJobLogAsync(job.Id, "Job completed successfully");

                // memory.md에 작업 결과 추가
                try
                {
                    await _memoryManager.AppendJobResultAsync(job, workingPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update memory.md for job {JobId}", job.Id);
                }
            }
            else
            {
                await _jobManager.UpdateJobStatusAsync(job.Id, JobStatus.Failed, errorMessage: error);
                await _jobManager.AddJobLogAsync(job.Id, $"Job failed with exit code {process.ExitCode}");

                // memory.md에 실패 기록 추가 (에러도 중요한 컨텍스트)
                try
                {
                    await _memoryManager.AppendJobResultAsync(job, workingPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update memory.md for job {JobId}", job.Id);
                }
            }

            return (success, output, error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing job {JobId}", job.Id);
            await _jobManager.UpdateJobStatusAsync(job.Id, JobStatus.Failed, errorMessage: ex.Message);
            await _jobManager.AddJobLogAsync(job.Id, $"Exception: {ex.Message}");
            return (false, string.Empty, ex.Message);
        }
        finally
        {
            // 8. Worktree 상태를 Active로 복구
            if (workingPath != null)
            {
                try
                {
                    await _worktreeManager.SetWorktreeStatusAsync(
                        job.ProjectName,
                        job.BigTaskName,
                        WorktreeStatus.Active);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error restoring worktree status for {JobId}", job.Id);
                }
            }

            // 스크립트 파일 삭제
            if (scriptPath != null)
            {
                _executionStrategy.DeleteScript(scriptPath);
            }
        }
    }
}
