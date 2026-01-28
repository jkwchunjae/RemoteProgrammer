using System.Diagnostics;
using Worker.Models;

namespace Worker.Services;

/// <summary>
/// Unix 환경 (Linux, macOS)에서 Claude Code를 실행하기 위한 전략
/// Bash를 사용하여 스크립트를 실행합니다
/// </summary>
public class UnixClaudeExecutionStrategy : IClaudeExecutionStrategy
{
    private readonly ILogger<UnixClaudeExecutionStrategy> _logger;

    public UnixClaudeExecutionStrategy(ILogger<UnixClaudeExecutionStrategy> logger)
    {
        _logger = logger;
    }

    public async Task<string> CreateExecutionScriptAsync(Job job)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"claude_job_{job.Id}.sh");

        // Bash 스크립트 생성
        // heredoc을 사용하여 여러 줄 description을 안전하게 전달
        var scriptContent = $@"#!/bin/bash

# Job: {job.Id}
# Project: {job.ProjectName}

cd ""{job.ProjectPath}""

# Claude Code 실행
# heredoc을 사용하여 여러 줄 텍스트를 안전하게 전달
claude <<'CLAUDE_INPUT_EOF'
{job.Description}
CLAUDE_INPUT_EOF

exit $?
";

        await File.WriteAllTextAsync(scriptPath, scriptContent);

        // 실행 권한 부여
        await SetExecutablePermissionAsync(scriptPath);

        _logger.LogInformation("Created bash script: {ScriptPath}", scriptPath);

        return scriptPath;
    }

    public ProcessStartInfo GetProcessStartInfo(string scriptPath, string workingDirectory)
    {
        return new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = scriptPath,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    public void DeleteScript(string scriptPath)
    {
        try
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
                _logger.LogInformation("Deleted script: {ScriptPath}", scriptPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete script: {ScriptPath}", scriptPath);
        }
    }

    private async Task SetExecutablePermissionAsync(string scriptPath)
    {
        try
        {
            var chmodPsi = new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var chmodProcess = Process.Start(chmodPsi);
            if (chmodProcess != null)
            {
                await chmodProcess.WaitForExitAsync();
                if (chmodProcess.ExitCode != 0)
                {
                    _logger.LogWarning("chmod failed with exit code {ExitCode}", chmodProcess.ExitCode);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set executable permission for {ScriptPath}", scriptPath);
        }
    }
}
