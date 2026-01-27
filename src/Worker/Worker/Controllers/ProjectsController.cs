using Microsoft.AspNetCore.Mvc;
using Worker.Models;
using Worker.Services;

namespace Worker.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProjectsController : ControllerBase
{
    private readonly ProjectManager _projectManager;

    public ProjectsController(ProjectManager projectManager)
    {
        _projectManager = projectManager;
    }

    [HttpGet]
    public async Task<ActionResult<List<Project>>> GetProjects()
    {
        var projects = await _projectManager.GetProjectsAsync();
        return Ok(projects);
    }

    [HttpGet("{name}")]
    public async Task<ActionResult<Project>> GetProject(string name)
    {
        var project = await _projectManager.GetProjectByNameAsync(name);
        if (project == null)
            return NotFound();

        return Ok(project);
    }
}
