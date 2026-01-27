using Microsoft.AspNetCore.Mvc;
using Worker.Models;
using Worker.Services;

namespace Worker.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly JobManager _jobManager;
    private readonly ProjectManager _projectManager;
    private readonly ClaudeCodeExecutor _claudeCodeExecutor;
    private readonly ILogger<JobsController> _logger;

    public JobsController(
        JobManager jobManager,
        ProjectManager projectManager,
        ClaudeCodeExecutor claudeCodeExecutor,
        ILogger<JobsController> logger)
    {
        _jobManager = jobManager;
        _projectManager = projectManager;
        _claudeCodeExecutor = claudeCodeExecutor;
        _logger = logger;
    }

    [HttpGet]
    public ActionResult<List<Job>> GetActiveJobs()
    {
        return Ok(_jobManager.GetActiveJobs());
    }

    [HttpGet("{id}")]
    public ActionResult<Job> GetJob(string id)
    {
        var job = _jobManager.GetJob(id);
        if (job == null)
            return NotFound();

        return Ok(job);
    }

    [HttpGet("history")]
    public async Task<ActionResult<List<Job>>> GetJobHistory([FromQuery] int limit = 50)
    {
        var history = await _jobManager.GetJobHistoryAsync(limit);
        return Ok(history);
    }

    [HttpPost]
    public async Task<ActionResult<Job>> CreateJob([FromBody] CreateJobRequest request)
    {
        var project = await _projectManager.GetProjectByNameAsync(request.ProjectName);
        if (project == null)
            return BadRequest($"Project '{request.ProjectName}' not found");

        var job = await _jobManager.CreateJobAsync(project.Name, project.Path, request.Description);

        // 백그라운드에서 작업 실행
        _ = Task.Run(async () =>
        {
            try
            {
                await _claudeCodeExecutor.ExecuteJobAsync(job);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing job {JobId}", job.Id);
            }
        });

        return CreatedAtAction(nameof(GetJob), new { id = job.Id }, job);
    }
}

public class CreateJobRequest
{
    public required string ProjectName { get; set; }
    public required string Description { get; set; }
}
