using Worker.Models;
using Worker.Utils;

namespace Worker.Services;

public class JobManager
{
    private readonly string _workspacePath;
    private readonly string _jobStatusPath;
    private readonly string _jobHistoryPath;
    private readonly ILogger<JobManager> _logger;
    private readonly ISerializer _serializer;
    private readonly Dictionary<string, Job> _activeJobs = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JobManager(IConfiguration configuration, ILogger<JobManager> logger, ISerializer serializer)
    {
        var configuredPath = configuration["WorkspacePath"] ?? "/workspace";
        // 절대경로로 변환
        _workspacePath = Path.GetFullPath(configuredPath);
        _jobStatusPath = Path.Combine(_workspacePath, "JobStatus");
        _jobHistoryPath = Path.Combine(_workspacePath, "JobHistory");
        _logger = logger;
        _serializer = serializer;

        Directory.CreateDirectory(_jobStatusPath);
        Directory.CreateDirectory(_jobHistoryPath);

        _logger.LogInformation("JobManager initialized - Workspace: {WorkspacePath}", _workspacePath);
        _logger.LogInformation("JobStatus path: {JobStatusPath}", _jobStatusPath);
        _logger.LogInformation("JobHistory path: {JobHistoryPath}", _jobHistoryPath);

        LoadActiveJobs();
    }

    public async Task<Job> CreateJobAsync(string projectName, string projectPath, string description, string bigTaskName)
    {
        await _lock.WaitAsync();
        try
        {
            var job = new Job
            {
                Id = Guid.NewGuid().ToString(),
                ProjectName = projectName,
                ProjectPath = projectPath,
                Description = description,
                BigTaskName = bigTaskName,
                Status = JobStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            _activeJobs[job.Id] = job;
            await SaveJobStatusAsync(job);

            _logger.LogInformation("Created job {JobId} for project {ProjectName} on branch {BigTaskName}", job.Id, projectName, bigTaskName);
            return job;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateJobStatusAsync(string jobId, JobStatus status, string? result = null, string? errorMessage = null)
    {
        await _lock.WaitAsync();
        try
        {
            if (!_activeJobs.TryGetValue(jobId, out var job))
            {
                _logger.LogWarning("Job {JobId} not found", jobId);
                return;
            }

            job.Status = status;

            if (status == JobStatus.Running && job.StartedAt == null)
            {
                job.StartedAt = DateTime.UtcNow;
            }

            if (status is JobStatus.Completed or JobStatus.Failed)
            {
                job.CompletedAt = DateTime.UtcNow;
                job.Result = result;
                job.ErrorMessage = errorMessage;

                await MoveToHistoryAsync(job);
                _activeJobs.Remove(jobId);
            }
            else
            {
                await SaveJobStatusAsync(job);
            }

            _logger.LogInformation("Updated job {JobId} status to {Status}", jobId, status);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddJobLogAsync(string jobId, string logMessage)
    {
        await _lock.WaitAsync();
        try
        {
            if (_activeJobs.TryGetValue(jobId, out var job))
            {
                job.Logs.Add($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {logMessage}");
                await SaveJobStatusAsync(job);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public Job? GetJob(string jobId)
    {
        _activeJobs.TryGetValue(jobId, out var job);
        return job;
    }

    public List<Job> GetActiveJobs()
    {
        return _activeJobs.Values.ToList();
    }

    public async Task<List<Job>> GetJobHistoryAsync(int limit = 50)
    {
        var historyFiles = Directory.GetFiles(_jobHistoryPath, "*.json")
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .Take(limit);

        var jobs = new List<Job>();

        foreach (var file in historyFiles)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var job = _serializer.Deserialize<Job>(json);
                if (job != null)
                {
                    jobs.Add(job);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load job history from {File}. File may be from older version.", Path.GetFileName(file));
            }
        }

        return jobs;
    }

    private void LoadActiveJobs()
    {
        if (!Directory.Exists(_jobStatusPath))
            return;

        var statusFiles = Directory.GetFiles(_jobStatusPath, "*.json");

        foreach (var file in statusFiles)
        {
            try
            {
                var json = File.ReadAllText(file);
                var job = _serializer.Deserialize<Job>(json);
                if (job != null)
                {
                    _activeJobs[job.Id] = job;
                    _logger.LogInformation("Loaded active job {JobId}", job.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load job from {File}. File may be from older version.", Path.GetFileName(file));
            }
        }
    }

    private async Task SaveJobStatusAsync(Job job)
    {
        try
        {
            var filePath = Path.Combine(_jobStatusPath, $"{job.Id}.json");
            var json = _serializer.Serialize(job);
            await File.WriteAllTextAsync(filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving job status for {JobId}", job.Id);
        }
    }

    private async Task MoveToHistoryAsync(Job job)
    {
        try
        {
            var statusFilePath = Path.Combine(_jobStatusPath, $"{job.Id}.json");
            var historyFilePath = Path.Combine(_jobHistoryPath, $"{job.Id}.json");

            var json = _serializer.Serialize(job);
            await File.WriteAllTextAsync(historyFilePath, json);

            if (File.Exists(statusFilePath))
            {
                File.Delete(statusFilePath);
            }

            _logger.LogInformation("Moved job {JobId} to history", job.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error moving job {JobId} to history", job.Id);
        }
    }
}
