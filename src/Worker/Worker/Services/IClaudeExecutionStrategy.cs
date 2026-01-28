using System.Diagnostics;
using Worker.Models;

namespace Worker.Services;

/// <summary>
/// Claude Code 실행을 위한 OS별 전략 인터페이스
/// </summary>
public interface IClaudeExecutionStrategy
{
    /// <summary>
    /// 작업 실행을 위한 스크립트를 생성합니다
    /// </summary>
    /// <param name="job">실행할 작업</param>
    /// <param name="workingPath">실제 작업 경로 (worktree 경로 또는 원본 경로)</param>
    /// <returns>스크립트 파일 경로</returns>
    Task<string> CreateExecutionScriptAsync(Job job, string workingPath);

    /// <summary>
    /// 스크립트 실행을 위한 ProcessStartInfo를 생성합니다
    /// </summary>
    /// <param name="scriptPath">스크립트 파일 경로</param>
    /// <param name="workingDirectory">작업 디렉토리</param>
    /// <returns>ProcessStartInfo 객체</returns>
    ProcessStartInfo GetProcessStartInfo(string scriptPath, string workingDirectory);

    /// <summary>
    /// 스크립트 파일을 삭제합니다
    /// </summary>
    /// <param name="scriptPath">스크립트 파일 경로</param>
    void DeleteScript(string scriptPath);
}
