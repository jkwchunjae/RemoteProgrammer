using System.Diagnostics;
using System.Text;
using Worker.Models;

namespace Worker.Services;

public class ClaudeCodeExecutor
{
    private readonly ILogger<ClaudeCodeExecutor> _logger;
    private readonly JobManager _jobManager;
    private readonly IClaudeExecutionStrategy _executionStrategy;

    public ClaudeCodeExecutor(
        ILogger<ClaudeCodeExecutor> logger,
        JobManager jobManager,
        IClaudeExecutionStrategy executionStrategy)
    {
        _logger = logger;
        _jobManager = jobManager;
        _executionStrategy = executionStrategy;
    }

    public async Task<(bool Success, string Output, string Error)> ExecuteJobAsync(Job job, CancellationToken cancellationToken = default)
    {
        string? scriptPath = null;

        try
        {
            await _jobManager.UpdateJobStatusAsync(job.Id, JobStatus.Running);
            await _jobManager.AddJobLogAsync(job.Id, $"Starting execution for project {job.ProjectName}");

            // OS별 전략을 사용하여 스크립트 생성
            scriptPath = await _executionStrategy.CreateExecutionScriptAsync(job);

            // OS별 전략을 사용하여 ProcessStartInfo 생성
            var psi = _executionStrategy.GetProcessStartInfo(scriptPath, job.ProjectPath);

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
            }
            else
            {
                await _jobManager.UpdateJobStatusAsync(job.Id, JobStatus.Failed, errorMessage: error);
                await _jobManager.AddJobLogAsync(job.Id, $"Job failed with exit code {process.ExitCode}");
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
            // 스크립트 파일 삭제
            if (scriptPath != null)
            {
                _executionStrategy.DeleteScript(scriptPath);
            }
        }
    }
}
