using LocalAgentProjectTeamOrchestration.Observability;
using LocalAgentProjectTeamOrchestration.Security;

namespace LocalAgentProjectTeamOrchestration.Tools;

internal sealed record ToolDescriptor(string Name, ToolPermission Permission, string Description);
internal sealed record ToolRequest(string RunId, string Input);
internal sealed record ToolResult(bool Success, string Output);

internal interface ITool
{
    ToolDescriptor Descriptor { get; }
    Task<ToolResult> InvokeAsync(ToolRequest request, CancellationToken cancellationToken);
}

internal sealed class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools;

    private ToolRegistry(IEnumerable<ITool> tools)
    {
        _tools = tools.ToDictionary(tool => tool.Descriptor.Name, StringComparer.OrdinalIgnoreCase);
    }

    public static ToolRegistry CreateDefault(AppPaths paths, ToolPermissionPolicy policy, JsonlTraceWriter traceWriter)
    {
        return new ToolRegistry([
            new ReadFileTool(paths, policy, traceWriter),
            new SearchCodeTool(paths, policy, traceWriter),
            new RunTestsTool(policy, traceWriter)
        ]);
    }

    public IReadOnlyList<ToolDescriptor> DescribeTools() => _tools.Values.Select(tool => tool.Descriptor).OrderBy(tool => tool.Name).ToList();

    public async Task<ToolResult> InvokeAsync(string name, string runId, string input, CancellationToken cancellationToken)
    {
        if (!_tools.TryGetValue(name, out var tool))
        {
            return new ToolResult(false, $"Tool not found: {name}");
        }

        return await tool.InvokeAsync(new ToolRequest(runId, input), cancellationToken);
    }
}

internal sealed class ReadFileTool(AppPaths paths, ToolPermissionPolicy policy, JsonlTraceWriter traceWriter) : ITool
{
    public ToolDescriptor Descriptor { get; } = new("ReadFile", ToolPermission.ReadOnly, "Reads a file from the allowed local workspace paths.");

    public async Task<ToolResult> InvokeAsync(ToolRequest request, CancellationToken cancellationToken)
    {
        var pathCheck = policy.CheckPath(paths, request.Input);
        if (!pathCheck.IsAllowed)
        {
            await TraceAsync(request, "blocked", pathCheck.Reason);
            return new ToolResult(false, pathCheck.Reason);
        }

        var fullPath = Path.GetFullPath(Path.Combine(paths.SolutionDirectory, request.Input));
        if (!File.Exists(fullPath))
        {
            await TraceAsync(request, "missing", fullPath);
            return new ToolResult(false, $"File not found: {request.Input}");
        }

        var text = await File.ReadAllTextAsync(fullPath, cancellationToken);
        var secretCheck = policy.CheckSecrets(text);
        if (!secretCheck.IsAllowed)
        {
            await TraceAsync(request, "blocked", secretCheck.Reason);
            return new ToolResult(false, secretCheck.Reason);
        }

        await TraceAsync(request, "ok", request.Input);
        return new ToolResult(true, text);
    }

    private Task TraceAsync(ToolRequest request, string status, string detail) =>
        traceWriter.WriteAsync(new ToolCallTrace(request.RunId, DateTimeOffset.Now, Descriptor.Name, Descriptor.Permission.ToString(), status, detail));
}

internal sealed class SearchCodeTool(AppPaths paths, ToolPermissionPolicy policy, JsonlTraceWriter traceWriter) : ITool
{
    public ToolDescriptor Descriptor { get; } = new("SearchCode", ToolPermission.ReadOnly, "Searches file names and text snippets in the allowed workspace.");

    public async Task<ToolResult> InvokeAsync(ToolRequest request, CancellationToken cancellationToken)
    {
        var rootCheck = policy.CheckPath(paths, ".");
        if (!rootCheck.IsAllowed)
        {
            await TraceAsync(request, "blocked", rootCheck.Reason);
            return new ToolResult(false, rootCheck.Reason);
        }

        var matches = new List<string>();
        foreach (var file in Directory.EnumerateFiles(paths.SolutionDirectory, "*", SearchOption.AllDirectories).Where(IsSearchable))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(paths.SolutionDirectory, file);
            if (relative.Contains(request.Input, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(relative);
                continue;
            }

            var text = await File.ReadAllTextAsync(file, cancellationToken);
            if (text.Contains(request.Input, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(relative);
            }
        }

        var output = matches.Count == 0 ? "No matches." : string.Join(Environment.NewLine, matches.Take(50));
        await TraceAsync(request, "ok", $"{matches.Count} matches");
        return new ToolResult(true, output);
    }

    private Task TraceAsync(ToolRequest request, string status, string detail) =>
        traceWriter.WriteAsync(new ToolCallTrace(request.RunId, DateTimeOffset.Now, Descriptor.Name, Descriptor.Permission.ToString(), status, detail));

    private static bool IsSearchable(string file)
    {
        var blockedSegments = new[]
        {
            $"{Path.DirectorySeparatorChar}.vs{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}data{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}logs{Path.DirectorySeparatorChar}"
        };
        return !blockedSegments.Any(segment => file.Contains(segment, StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed class RunTestsTool(ToolPermissionPolicy policy, JsonlTraceWriter traceWriter) : ITool
{
    public ToolDescriptor Descriptor { get; } = new("RunTests", ToolPermission.Execute, "Mock test runner that demonstrates command permission checks.");

    public async Task<ToolResult> InvokeAsync(ToolRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var command = string.IsNullOrWhiteSpace(request.Input) ? "dotnet test" : request.Input.Trim();
        var commandCheck = policy.CheckCommand(command);
        if (!commandCheck.IsAllowed)
        {
            await TraceAsync(request, "blocked", commandCheck.Reason);
            return new ToolResult(false, commandCheck.Reason);
        }

        await TraceAsync(request, "mock-ok", command);
        return new ToolResult(true, $"Mock execution accepted by whitelist: {command}");
    }

    private Task TraceAsync(ToolRequest request, string status, string detail) =>
        traceWriter.WriteAsync(new ToolCallTrace(request.RunId, DateTimeOffset.Now, Descriptor.Name, Descriptor.Permission.ToString(), status, detail));
}
