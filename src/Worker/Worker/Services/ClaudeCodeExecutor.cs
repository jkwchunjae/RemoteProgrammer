using System.Diagnostics;
using System.Text;
using Worker.Models;

namespace Worker.Services;

public class ClaudeCodeExecutor
{
    private readonly ILogger<ClaudeCodeExecutor> _logger;
    private readonly JobManager _jobManager;

    public ClaudeCodeExecutor(ILogger<ClaudeCodeExecutor> logger, JobManager jobManager)
    {
        _logger = logger;
        _jobManager = jobManager;
    }

    public async Task<(bool Success, string Output, string Error)> ExecuteJobAsync(Job job, CancellationToken cancellationToken = default)
    {
        try
        {
            await _jobManager.UpdateJobStatusAsync(job.Id, JobStatus.Running);
            await _jobManager.AddJobLogAsync(job.Id, $"Starting execution for project {job.ProjectName}");

            // Claude Code 실행을 위한 스크립트 생성
            var scriptPath = await CreateExecutionScriptAsync(job);

            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = scriptPath,
                WorkingDirectory = job.ProjectPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

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

            // 스크립트 파일 삭제
            try
            {
                File.Delete(scriptPath);
            }
            catch { }

            return (success, output, error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing job {JobId}", job.Id);
            await _jobManager.UpdateJobStatusAsync(job.Id, JobStatus.Failed, errorMessage: ex.Message);
            await _jobManager.AddJobLogAsync(job.Id, $"Exception: {ex.Message}");
            return (false, string.Empty, ex.Message);
        }
    }

    private async Task<string> CreateExecutionScriptAsync(Job job)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"claude_job_{job.Id}.sh");

        var scriptContent = $@"#!/bin/bash

# Job: {job.Id}
# Project: {job.ProjectName}
# Description: {job.Description}

cd ""{job.ProjectPath}""

# Claude Code 실행
# 사용자 요청사항을 Claude에게 전달
echo ""{EscapeForBash(job.Description)}"" | claude --no-confirm

exit $?
";

        await File.WriteAllTextAsync(scriptPath, scriptContent);

        // 실행 권한 부여
        var chmodPsi = new ProcessStartInfo
        {
            FileName = "chmod",
            Arguments = $"+x {scriptPath}",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var chmodProcess = Process.Start(chmodPsi);
        if (chmodProcess != null)
        {
            await chmodProcess.WaitForExitAsync();
        }

        return scriptPath;
    }

    private string EscapeForBash(string input)
    {
        return input.Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`");
    }
}
