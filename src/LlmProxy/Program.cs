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
builder.Services.AddSingleton<ConversationLocks>();
builder.Services.AddSingleton<ConversationIdResolver>();
builder.Services.AddSingleton<AuditBodyCapture>();

// The logger holds no per-request state, so one instance serves the whole process: its constructor creates the
// conversation directory once instead of on every proxied request.
builder.Services.AddSingleton<IConversationLogger, FileConversationLogger>();
builder.Services.AddScoped<IConversationReader, FileConversationReader>();

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

// Load the conversation index here rather than on the first proxied request, so the whole first-read cost does not
// land on a user. A failure is not fatal and is not treated as loaded: the index keeps its rebuild and retries
// after its own back-off instead of re-running it on every request.
var conversationIndex = app.Services.GetRequiredService<ConversationIndex>();
try
{
    await conversationIndex.EnsureLoadedAsync();
}
catch (Exception ex)
{
    startupLogger.LogWarning(ex, "Conversation index could not be loaded at startup; the audit log will retry it later");
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

// Operator pages (conversation list and replay) are plain static files under wwwroot.
app.UseStaticFiles();

app.MapGet("/", () => Results.Redirect("/admin"));

// Friendly entry points for the conversation pages; the pages read their parameters from the query string.
app.MapGet("/admin/conversations", () => Results.Redirect("/conversations.html"));
app.MapGet("/admin/conversation/{id}", (string id) => Results.Redirect($"/conversation.html?id={Uri.EscapeDataString(id)}"));

app.MapControllers();

app.Run();
