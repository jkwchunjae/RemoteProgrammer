using System.Diagnostics;
using Worker.Models;

namespace Worker.Services;

/// <summary>
/// Windows 환경에서 Claude Code를 실행하기 위한 전략
/// PowerShell을 사용하여 스크립트를 실행합니다
/// </summary>
public class WindowsClaudeExecutionStrategy : IClaudeExecutionStrategy
{
    private readonly ILogger<WindowsClaudeExecutionStrategy> _logger;

    public WindowsClaudeExecutionStrategy(ILogger<WindowsClaudeExecutionStrategy> logger)
    {
        _logger = logger;
    }

    public async Task<string> CreateExecutionScriptAsync(Job job, string workingPath)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"claude_job_{job.Id}.ps1");

        // PowerShell 스크립트 생성
        // Here-String (@' '@)을 사용하여 여러 줄 텍스트를 안전하게 전달
        var scriptContent = $@"# Job: {job.Id}
# Project: {job.ProjectName}
# BigTask: {job.BigTaskName}

Set-Location ""{workingPath}""

# Claude Code 실행
# Here-String을 사용하여 여러 줄 텍스트를 안전하게 전달
$description = @'
{job.Description}
'@

$description | claude --allow-dangerously-skip-permissions --dangerously-skip-permissions

exit $LASTEXITCODE
";

        await File.WriteAllTextAsync(scriptPath, scriptContent);
        _logger.LogInformation("Created PowerShell script: {ScriptPath}", scriptPath);

        return scriptPath;
    }

    public ProcessStartInfo GetProcessStartInfo(string scriptPath, string workingDirectory)
    {
        return new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
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
}
