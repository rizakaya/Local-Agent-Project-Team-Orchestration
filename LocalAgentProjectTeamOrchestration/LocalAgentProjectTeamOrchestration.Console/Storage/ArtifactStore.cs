using System.Text;
using LocalAgentProjectTeamOrchestration.State;

namespace LocalAgentProjectTeamOrchestration.Storage;

internal sealed class ArtifactStore(AppPaths paths)
{
    public async Task SaveRoleOutputAsync(AgentRole role, string content)
    {
        var directory = RoleDirectory(role);
        Directory.CreateDirectory(directory);
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var fileName = role switch
        {
            AgentRole.Analyst => "requirements",
            AgentRole.Developer => "implementation",
            AgentRole.Tester => "test-plan",
            AgentRole.Lead => "decision",
            _ => "artifact"
        };

        var markdown = new StringBuilder()
            .AppendLine($"# {role} Output")
            .AppendLine()
            .AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}")
            .AppendLine()
            .AppendLine(content.Trim())
            .AppendLine()
            .ToString();

        await File.WriteAllTextAsync(Path.Combine(directory, "latest.md"), markdown);
        await File.WriteAllTextAsync(Path.Combine(directory, $"{fileName}-{timestamp}.md"), markdown);
    }

    public async Task<IReadOnlyList<string>> SaveDeveloperCodeBlocksAsync(string developerOutput)
    {
        Directory.CreateDirectory(paths.ProjectOutputDirectory);
        var written = new List<string>();

        foreach (var block in ExtractFileBlocks(developerOutput))
        {
            var safePath = ResolveProjectOutputPath(block.RelativePath);
            if (safePath is null)
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(safePath)!);
            await File.WriteAllTextAsync(safePath, block.Content);
            written.Add(Path.GetRelativePath(paths.OutputsDirectory, safePath));
        }

        return written;
    }

    public string Describe()
    {
        return string.Join(Environment.NewLine, [
            $"Analysis:  {paths.AnalysisOutputDirectory}",
            $"Developer: {paths.DeveloperOutputDirectory}",
            $"Tester:    {paths.TesterOutputDirectory}",
            $"Lead:      {paths.LeadOutputDirectory}",
            $"Project:   {paths.ProjectOutputDirectory}",
            "",
            "Developer code blocks can be written as:",
            "```file:src/MyProject/Program.cs",
            "// code",
            "```"
        ]);
    }

    private string RoleDirectory(AgentRole role) => role switch
    {
        AgentRole.Analyst => paths.AnalysisOutputDirectory,
        AgentRole.Developer => paths.DeveloperOutputDirectory,
        AgentRole.Tester => paths.TesterOutputDirectory,
        AgentRole.Lead => paths.LeadOutputDirectory,
        _ => paths.OutputsDirectory
    };

    private string? ResolveProjectOutputPath(string relativePath)
    {
        var clean = relativePath.Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(clean) || clean.Contains(".."))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(Path.Combine(paths.ProjectOutputDirectory, clean));
        var root = Path.GetFullPath(paths.ProjectOutputDirectory);
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
    }

    private static IEnumerable<FileBlock> ExtractFileBlocks(string text)
    {
        using var reader = new StringReader(text);
        string? line;
        string? currentPath = null;
        var builder = new StringBuilder();

        while ((line = reader.ReadLine()) is not null)
        {
            if (currentPath is null)
            {
                if (line.StartsWith("```file:", StringComparison.OrdinalIgnoreCase))
                {
                    currentPath = line["```file:".Length..].Trim();
                    builder.Clear();
                }

                continue;
            }

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                yield return new FileBlock(currentPath, builder.ToString());
                currentPath = null;
                builder.Clear();
                continue;
            }

            builder.AppendLine(line);
        }
    }

    private sealed record FileBlock(string RelativePath, string Content);
}
