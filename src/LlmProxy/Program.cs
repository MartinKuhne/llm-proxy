using Serilog;
using LlmProxy.Application;
using LlmProxy.Application.Configuration;
using LlmProxy.Application.Services;
using System.Text.Json;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

// Resolve providers.json: explicit env var > ContentRoot > ~/.config/llm-proxy/
var providersFile =
    Environment.GetEnvironmentVariable("PROVIDERS_FILE") ??
    new[]
    {
        Path.Combine(builder.Environment.ContentRootPath, "providers.json"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "llm-proxy", "providers.json")
    }
    .FirstOrDefault(File.Exists)
    ?? Path.Combine(builder.Environment.ContentRootPath, "providers.json");

ProvidersConfig providersConfig = new ProvidersConfig();
if (File.Exists(providersFile))
{
    try
    {
        providersConfig = JsonSerializer.Deserialize<ProvidersConfig>(
            File.ReadAllText(providersFile),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new ProvidersConfig();
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Could not read providers configuration from {ProvidersFile}", providersFile);
    }
}
else
{
    Log.Warning("Providers file {ProvidersFile} does not exist or is a directory. Starting with empty configuration.", providersFile);
}
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration));

builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddSingleton(providersConfig);
builder.Services.AddHttpClient();
builder.Services.ConfigureHttpClientDefaults(b =>
    b.ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(600)));

// Conversation audit log: the optional "Conversations" section overrides the defaults. The configured directory is
// published through AppPaths so the logger and the index always read and write the same folder.
var conversationOptions = builder.Configuration.GetSection("Conversations").Get<ConversationOptions>() ?? new ConversationOptions();
if (!string.IsNullOrWhiteSpace(conversationOptions.Directory))
{
    AppPaths.ConversationDirectory = conversationOptions.Directory;
}

builder.Services.AddSingleton(conversationOptions);
builder.Services.AddSingleton<ConversationIndex>();
builder.Services.AddScoped<IConversationLogger, FileConversationLogger>();

builder.Services.AddScoped<ITokenCountLogger, FileTokenCountLogger>();
builder.Services.AddScoped<IUsageReader, FileUsageReader>();
builder.Services.AddScoped<IProxyService, ProxyService>();
builder.Services.AddSingleton<IProviderManager, ProviderManager>();
builder.Services.AddSingleton<IOpenRouterModelLoader, OpenRouterModelLoader>();

var app = builder.Build();

// Validate provider configuration before starting
providersConfig.Validate();

var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
startupLogger.LogInformation("Reading providers from {ProvidersFile}", providersFile);

var openRouterLoader = app.Services.GetRequiredService<IOpenRouterModelLoader>();
await openRouterLoader.LoadFreeModelsAsync(providersConfig);

var providerManager = app.Services.GetRequiredService<IProviderManager>();
var providers = providerManager.GetAllProviders().ToList();
if (providers.Count == 0)
{
    startupLogger.LogWarning("No providers configured. Create {ProvidersFile} to add providers", providersFile);
}
foreach (var provider in providers)
{
    startupLogger.LogInformation(
        "Provider configured: {Name} {BaseUrl} model={Model} smct={SendMaxCompletionTokens} priority={Priority}",
        provider.Name, provider.BaseUrl, provider.Model, provider.SendMaxCompletionTokens, provider.Priority);
}

// Global exception handler for unhandled exceptions
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            """{"error":{"message":"An unexpected error occurred","type":"server_error","param":null,"code":"500"}}""");
    });
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseSerilogRequestLogging();

app.MapGet("/", () => Results.Redirect("/admin"));

app.MapControllers();

app.Run();
