using Worker.Services;
using Worker.Utils;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddControllers();

// Serializer 등록
builder.Services.AddSingleton<ISerializer, Json>();

// Worker 서비스 등록
builder.Services.AddSingleton<ProjectManager>();
builder.Services.AddSingleton<JobManager>();
builder.Services.AddSingleton<GitWorktreeManager>();
builder.Services.AddSingleton<MemoryManager>();
builder.Services.AddSingleton<ClaudeCodeExecutor>();

// OS별 Claude 실행 전략 등록
if (OperatingSystem.IsWindows())
{
    builder.Services.AddSingleton<IClaudeExecutionStrategy, WindowsClaudeExecutionStrategy>();
}
else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
{
    builder.Services.AddSingleton<IClaudeExecutionStrategy, UnixClaudeExecutionStrategy>();
}
else
{
    throw new PlatformNotSupportedException($"Unsupported operating system: {Environment.OSVersion.Platform}");
}

// WebSocket 서비스는 RelayServerUrl이 설정되어 있을 때만 실행
if (!string.IsNullOrEmpty(builder.Configuration["RelayServerUrl"]))
{
    builder.Services.AddHostedService<WebSocketService>();
}

// CORS 설정 (로컬 테스트용)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseCors();
app.UseRouting();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();
app.MapControllers();

app.Run();
