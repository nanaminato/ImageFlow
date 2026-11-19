using ImageFlow;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables().AddCommandLine(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddSingleton<EngineOptions>(s =>
{
    var o = new EngineOptions(); s.GetRequiredService<IConfiguration>().GetSection("Engine").Bind(o);
    o.Validate();
    o.DataDirectory = Path.GetFullPath(o.DataDirectory, s.GetRequiredService<IWebHostEnvironment>().ContentRootPath);
    Directory.CreateDirectory(Path.Combine(o.DataDirectory, "images"));
    return o;
});
builder.Services.AddSingleton<QueueGate>();
builder.Services.AddScoped<IGenerationRepository, GenerationRepository>();
builder.Services.AddScoped<PromptPlanService>();
builder.Services.AddScoped<JobService>();
builder.Services.AddDbContext<QueueDb>((s, x) => x.UseSqlite($"Data Source={Path.Combine(s.GetRequiredService<EngineOptions>().DataDirectory, "imageflow.db")}"));
builder.Services.AddSingleton<MockProvider>();
builder.Services.AddHttpClient<OpenAiProvider>(c => c.Timeout = TimeSpan.FromMinutes(10))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<IPromptPlanningProvider>(s => s.GetRequiredService<EngineOptions>().Provider == "Mock" ? s.GetRequiredService<MockProvider>() : s.GetRequiredService<OpenAiProvider>());
builder.Services.AddSingleton<IImageGenerationProvider>(s => s.GetRequiredService<EngineOptions>().Provider == "Mock" ? s.GetRequiredService<MockProvider>() : s.GetRequiredService<OpenAiProvider>());
builder.Services.AddHostedService<QueueWorker>();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddCors(x => x.AddDefaultPolicy(p => p.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
var options = app.Services.GetRequiredService<EngineOptions>();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<QueueDb>();
    await db.Database.MigrateAsync();
    await QueueWorker.Recover(db, CancellationToken.None);
}
app.UseExceptionHandler();
app.UseCors();
app.MapOpenApi();
var hasWebRoot = Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "wwwroot"));
if (hasWebRoot)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}
app.MapGet("/health", async (QueueDb db) => await db.Database.CanConnectAsync() ? Results.Ok(new { status = "healthy" }) : Results.StatusCode(503));
var api = app.MapGroup("/api/v1");
api.MapGet("/settings", (EngineOptions options) => new { provider = options.Provider, promptModel = options.PromptModel, imageModel = options.ImageModel, maxConcurrency = options.MaxConcurrency, maxPrompts = 20, mode = "Finite" });

api.MapPlanEndpoints();
api.MapJobEndpoints();
api.MapMediaEndpoints();
api.MapEventEndpoints();
if (hasWebRoot) app.MapFallbackToFile("index.html");
app.Run();
public partial class Program { }
