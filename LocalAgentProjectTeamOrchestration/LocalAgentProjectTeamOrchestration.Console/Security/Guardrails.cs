using LocalAgentProjectTeamOrchestration.Observability;
using LocalAgentProjectTeamOrchestration.Tools;

namespace LocalAgentProjectTeamOrchestration.Security;

internal enum ToolPermission
{
    ReadOnly,
    Execute,
    Write,
    Dangerous
}

internal sealed class GuardrailService(GuardrailConfig config)
{
    public GuardrailResult CheckInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return GuardrailResult.Blocked("Input cannot be empty.");
        }

        if (input.Length > config.MaxInputCharacters)
        {
            return GuardrailResult.Blocked($"Input is too long. Limit: {config.MaxInputCharacters} characters.");
        }

        return GuardrailResult.Allowed();
    }

    public GuardrailResult CheckPromptBudget(string prompt)
    {
        if (prompt.Length > config.MaxEstimatedPromptCharacters)
        {
            return GuardrailResult.Blocked($"Estimated prompt is too large. Limit: {config.MaxEstimatedPromptCharacters} characters.");
        }

        return GuardrailResult.Allowed();
    }

    public GuardrailResult CheckOutput(string output)
    {
        if (output.Length > config.MaxOutputCharacters)
        {
            return GuardrailResult.Blocked($"Output is too long. Limit: {config.MaxOutputCharacters} characters.");
        }

        return GuardrailResult.Allowed();
    }
}

internal sealed record GuardrailResult(bool IsAllowed, string Reason)
{
    public static GuardrailResult Allowed() => new(true, "allowed");
    public static GuardrailResult Blocked(string reason) => new(false, reason);
}

internal sealed class ToolPermissionPolicy
{
    private readonly string[] _allowedPaths;
    private readonly string[] _secretBlocklist;
    private readonly string[] _commandWhitelist;

    public ToolPermissionPolicy(SecurityConfig config)
    {
        _allowedPaths = config.AllowedPaths;
        _secretBlocklist = config.SecretBlocklist;
        _commandWhitelist = config.CommandWhitelist;
    }

    public GuardrailResult CheckPath(AppPaths paths, string requestedPath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(paths.SolutionDirectory, requestedPath));
        var allowedRoots = _allowedPaths
            .Select(paths.ResolveFromSolution)
            .Select(Path.GetFullPath)
            .ToArray();

        var allowed = allowedRoots.Any(root => fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        return allowed ? GuardrailResult.Allowed() : GuardrailResult.Blocked($"Path is outside allowlist: {requestedPath}");
    }

    public GuardrailResult CheckSecrets(string text)
    {
        var hit = _secretBlocklist.FirstOrDefault(secret => text.Contains(secret, StringComparison.OrdinalIgnoreCase));
        return hit is null ? GuardrailResult.Allowed() : GuardrailResult.Blocked($"Content matched secret blocklist: {hit}");
    }

    public GuardrailResult CheckCommand(string command)
    {
        var allowed = _commandWhitelist.Any(allowedCommand => command.StartsWith(allowedCommand, StringComparison.OrdinalIgnoreCase));
        return allowed ? GuardrailResult.Allowed() : GuardrailResult.Blocked($"Command is not whitelisted: {command}");
    }
}

internal sealed class ToolServer(ToolRegistry registry)
{
    public IReadOnlyList<ToolDescriptor> DiscoverTools() => registry.DescribeTools();
}
