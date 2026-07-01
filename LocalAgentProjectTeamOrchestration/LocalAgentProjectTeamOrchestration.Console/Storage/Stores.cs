using System.Text;
using System.Text.Json;
using LocalAgentProjectTeamOrchestration.State;

namespace LocalAgentProjectTeamOrchestration.Storage;

internal sealed class SessionStore(AppPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<SessionState> LoadAsync()
    {
        paths.EnsureDirectories();
        if (!File.Exists(paths.SessionStatePath))
        {
            var initial = new SessionState();
            await SaveAsync(initial);
            return initial;
        }

        var readPath = paths.SessionStatePath;
        var tempRecoveryPath = paths.SessionStatePath + ".tmp";
        if (new FileInfo(paths.SessionStatePath).Length == 0 && File.Exists(tempRecoveryPath))
        {
            readPath = tempRecoveryPath;
        }

        await using var stream = File.OpenRead(readPath);
        return await JsonSerializer.DeserializeAsync<SessionState>(stream, JsonOptions) ?? new SessionState();
    }

    public async Task SaveAsync(SessionState state)
    {
        paths.EnsureDirectories();
        var next = state with { UpdatedAt = DateTimeOffset.Now };
        var tempPath = $"{paths.SessionStatePath}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, next, JsonOptions);
        }

        File.Move(tempPath, paths.SessionStatePath, overwrite: true);
    }

    public async Task<SessionState> ArchiveAndResetAsync()
    {
        paths.EnsureDirectories();
        if (File.Exists(paths.SessionStatePath))
        {
            var archiveName = $"session-state-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json";
            File.Copy(paths.SessionStatePath, Path.Combine(paths.ArchiveDirectory, archiveName), overwrite: false);
        }

        var fresh = new SessionState();
        await SaveAsync(fresh);
        return fresh;
    }
}

internal sealed class MemoryStore(AppPaths paths)
{
    public async Task EnsureExistsAsync()
    {
        paths.EnsureDirectories();
        if (File.Exists(paths.MemoryPath))
        {
            return;
        }

        var content = new StringBuilder()
            .AppendLine("# Project Memory")
            .AppendLine()
            .AppendLine("## Preferences")
            .AppendLine("- Local models are preferred.")
            .AppendLine("- Keep state in local JSON/Markdown files.")
            .AppendLine()
            .AppendLine("## Decisions")
            .AppendLine("- Use a console application for the first orchestration version.")
            .AppendLine()
            .AppendLine("## Open Questions")
            .AppendLine("- None")
            .AppendLine()
            .AppendLine("## Next Step")
            .AppendLine("- Define an idea with `/idea <text>` and run `/analyze` or `/run`.")
            .ToString();
        await File.WriteAllTextAsync(paths.MemoryPath, content);
    }

    public async Task<string> ReadAsync()
    {
        await EnsureExistsAsync();
        return await File.ReadAllTextAsync(paths.MemoryPath);
    }

    public async Task AppendDecisionAsync(string title, string content)
    {
        await EnsureExistsAsync();
        var entry = new StringBuilder()
            .AppendLine()
            .AppendLine($"## {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} - {title}")
            .AppendLine(content.Trim())
            .AppendLine()
            .ToString();
        await File.AppendAllTextAsync(paths.MemoryPath, entry);
    }
}
