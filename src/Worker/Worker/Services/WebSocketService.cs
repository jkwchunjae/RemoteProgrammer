using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Worker.Models;

namespace Worker.Services;

public class WebSocketService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<WebSocketService> _logger;
    private readonly ProjectManager _projectManager;
    private readonly JobManager _jobManager;
    private readonly ClaudeCodeExecutor _claudeCodeExecutor;
    private ClientWebSocket? _webSocket;
    private readonly string _serverUrl;
    private readonly Dictionary<string, TaskCompletionSource<string>> _pendingUserInputs = new();

    public WebSocketService(
        IConfiguration configuration,
        ILogger<WebSocketService> logger,
        ProjectManager projectManager,
        JobManager jobManager,
        ClaudeCodeExecutor claudeCodeExecutor)
    {
        _configuration = configuration;
        _logger = logger;
        _projectManager = projectManager;
        _jobManager = jobManager;
        _claudeCodeExecutor = claudeCodeExecutor;
        _serverUrl = configuration["RelayServerUrl"] ?? "ws://localhost:5000/worker";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndListenAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebSocket connection error");
                await Task.Delay(5000, stoppingToken); // 재연결 대기
            }
        }
    }

    private async Task ConnectAndListenAsync(CancellationToken cancellationToken)
    {
        _webSocket = new ClientWebSocket();

        _logger.LogInformation("Connecting to relay server: {Url}", _serverUrl);
        await _webSocket.ConnectAsync(new Uri(_serverUrl), cancellationToken);
        _logger.LogInformation("Connected to relay server");

        // 연결 후 현재 상태 보고
        await SendWorkerStatusAsync();

        var buffer = new byte[1024 * 4];

        while (_webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                _logger.LogInformation("Server closed connection");
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server closed", cancellationToken);
                break;
            }

            var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
            await HandleMessageAsync(message);
        }
    }

    private async Task HandleMessageAsync(string messageJson)
    {
        try
        {
            var message = JsonSerializer.Deserialize<WorkerMessage>(messageJson);
            if (message == null) return;

            _logger.LogInformation("Received message type: {Type}", message.Type);

            switch (message.Type)
            {
                case "JobRequest":
                    await HandleJobRequestAsync(message.Data);
                    break;

                case "UserInputResponse":
                    await HandleUserInputResponseAsync(message.Data);
                    break;

                case "StatusRequest":
                    await SendWorkerStatusAsync();
                    break;

                default:
                    _logger.LogWarning("Unknown message type: {Type}", message.Type);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling message: {Message}", messageJson);
        }
    }

    private async Task HandleJobRequestAsync(object? data)
    {
        try
        {
            var jobRequest = JsonSerializer.Deserialize<JobRequest>(JsonSerializer.Serialize(data));
            if (jobRequest == null) return;

            _logger.LogInformation("Received job request: {JobId} for project {ProjectName}", jobRequest.JobId, jobRequest.ProjectName);

            var project = await _projectManager.GetProjectByNameAsync(jobRequest.ProjectName);
            if (project == null)
            {
                _logger.LogWarning("Project not found: {ProjectName}", jobRequest.ProjectName);
                await SendJobResponseAsync(new JobResponse
                {
                    JobId = jobRequest.JobId,
                    Status = "Failed",
                    ErrorMessage = $"Project '{jobRequest.ProjectName}' not found"
                });
                return;
            }

            var job = await _jobManager.CreateJobAsync(project.Name, project.Path, jobRequest.Description);

            // 백그라운드에서 작업 실행
            _ = Task.Run(async () =>
            {
                var (success, output, error) = await _claudeCodeExecutor.ExecuteJobAsync(job);

                await SendJobResponseAsync(new JobResponse
                {
                    JobId = job.Id,
                    Status = success ? "Completed" : "Failed",
                    Result = success ? output : null,
                    ErrorMessage = success ? null : error
                });

                await SendWorkerStatusAsync();
            });

            await SendJobResponseAsync(new JobResponse
            {
                JobId = job.Id,
                Status = "Running"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling job request");
        }
    }

    private async Task HandleUserInputResponseAsync(object? data)
    {
        try
        {
            var response = JsonSerializer.Deserialize<UserInputResponse>(JsonSerializer.Serialize(data));
            if (response == null) return;

            if (_pendingUserInputs.TryGetValue(response.JobId, out var tcs))
            {
                tcs.SetResult(response.Answer);
                _pendingUserInputs.Remove(response.JobId);
                _logger.LogInformation("Received user input for job {JobId}", response.JobId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling user input response");
        }
    }

    public async Task<string> RequestUserInputAsync(string jobId, string question)
    {
        var tcs = new TaskCompletionSource<string>();
        _pendingUserInputs[jobId] = tcs;

        var request = new UserInputRequest
        {
            JobId = jobId,
            Question = question
        };

        await SendMessageAsync(new WorkerMessage
        {
            Type = "UserInputRequest",
            Data = request
        });

        return await tcs.Task;
    }

    private async Task SendWorkerStatusAsync()
    {
        try
        {
            var projects = await _projectManager.GetProjectsAsync();
            var activeJobs = _jobManager.GetActiveJobs();

            var status = new WorkerStatus
            {
                Projects = projects.Select(p => p.Name).ToList(),
                RunningJobs = activeJobs.Select(j => new JobSummary
                {
                    JobId = j.Id,
                    ProjectName = j.ProjectName,
                    Status = j.Status.ToString()
                }).ToList()
            };

            await SendMessageAsync(new WorkerMessage
            {
                Type = "WorkerStatus",
                Data = status
            });

            _logger.LogInformation("Sent worker status: {ProjectCount} projects, {JobCount} active jobs",
                status.Projects.Count, status.RunningJobs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending worker status");
        }
    }

    private async Task SendJobResponseAsync(JobResponse response)
    {
        await SendMessageAsync(new WorkerMessage
        {
            Type = "JobResponse",
            Data = response
        });
    }

    private async Task SendMessageAsync(WorkerMessage message)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            _logger.LogWarning("WebSocket is not open, cannot send message");
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_webSocket?.State == WebSocketState.Open)
        {
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Service stopping", cancellationToken);
        }

        await base.StopAsync(cancellationToken);
    }
}
