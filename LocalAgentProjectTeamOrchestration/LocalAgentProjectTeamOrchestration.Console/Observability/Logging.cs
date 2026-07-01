using System.Text.Json;
using LocalAgentProjectTeamOrchestration.State;

namespace LocalAgentProjectTeamOrchestration.Observability;

internal sealed record AgentRun(string RunId, DateTimeOffset StartedAt, string Command, string Idea);
internal sealed record AgentStep(string RunId, string StepId, DateTimeOffset Timestamp, string Agent, string Model, string Status, string Summary);
internal sealed record ToolCallTrace(string RunId, DateTimeOffset Timestamp, string ToolName, string Permission, string Status, string Detail);

internal sealed class MarkdownConversationLogger
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MarkdownConversationLogger(AppPaths paths)
    {
        _path = System.IO.Path.Combine(paths.LogsDirectory, $"conversation-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.md");
    }

    public string Path => _path;

    public async Task WriteAsync(string title, string content)
    {
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            await File.AppendAllTextAsync(_path, $"## {DateTimeOffset.Now:HH:mm:ss} {title}{Environment.NewLine}{Environment.NewLine}{content.Trim()}{Environment.NewLine}{Environment.NewLine}");
        }
        finally
        {
            _gate.Release();
        }
    }
}

internal sealed class JsonlTraceWriter(AppPaths paths)
{
    private readonly string _path = Path.Combine(paths.LogsDirectory, $"traces-{DateTimeOffset.Now:yyyyMMdd}.jsonl");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task WriteAsync<T>(T item)
    {
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            var json = JsonSerializer.Serialize(item, JsonOptions);
            await File.AppendAllTextAsync(_path, json + Environment.NewLine);
        }
        finally
        {
            _gate.Release();
        }
    }
}

internal sealed class ConsoleRenderer(MarkdownConversationLogger markdownLogger, JsonlTraceWriter traceWriter)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task SystemAsync(string message)
    {
        await WriteBlockAsync("System", "", message, ConsoleColor.Cyan);
    }

    public async Task UserAsync(string message)
    {
        await WriteBlockAsync("User", "", message, ConsoleColor.Green);
    }

    public async Task AgentAsync(ConversationMessage message)
    {
        await WriteBlockAsync(message.Role.ToString(), message.Model, message.Content, ConsoleColor.Yellow);
    }

    public async Task ErrorAsync(string message, string runId = "system")
    {
        await WriteBlockAsync("Error", "", message, ConsoleColor.Red);
        await traceWriter.WriteAsync(new AgentStep(runId, Guid.NewGuid().ToString("N"), DateTimeOffset.Now, "System", "", "error", message));
    }

    public async Task WriteBlockAsync(string title, string model, string content, ConsoleColor color)
    {
        await _gate.WaitAsync();
        try
        {
            var previous = System.Console.ForegroundColor;
            System.Console.ForegroundColor = color;
            System.Console.WriteLine();
            System.Console.WriteLine(model.Length == 0 ? $"[{DateTimeOffset.Now:HH:mm:ss}] {title}" : $"[{DateTimeOffset.Now:HH:mm:ss}] {title} ({model})");
            System.Console.ForegroundColor = previous;
            System.Console.WriteLine(content.Trim());
            await markdownLogger.WriteAsync(model.Length == 0 ? title : $"{title} ({model})", content);
        }
        finally
        {
            _gate.Release();
        }
    }
}
