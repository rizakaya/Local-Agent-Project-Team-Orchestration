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

    public async Task<IReadOnlyList<string>> SaveDeveloperCodeBlocksAsync(string developerOutput, string projectFolderName)
    {
        Directory.CreateDirectory(paths.ProjectOutputDirectory);
        var written = new List<string>();

        foreach (var block in ExtractFileBlocks(developerOutput))
        {
            var safePath = ResolveProjectOutputPath(Path.Combine(projectFolderName, block.RelativePath));
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
        var projectFiles = Directory.Exists(paths.ProjectOutputDirectory)
            ? Directory.EnumerateFiles(paths.ProjectOutputDirectory, "*", SearchOption.AllDirectories)
                .Where(IsSourceArtifact)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList()
            : [];
        var latestProjectFiles = projectFiles
            .Take(10)
            .Select(file => $"- {Path.GetRelativePath(paths.ProjectOutputDirectory, file)}")
            .DefaultIfEmpty("- No generated project files yet.");

        return string.Join(Environment.NewLine, [
            $"Analysis:  {paths.AnalysisOutputDirectory}",
            $"Developer: {paths.DeveloperOutputDirectory}",
            $"Tester:    {paths.TesterOutputDirectory}",
            $"Lead:      {paths.LeadOutputDirectory}",
            $"Project:   {paths.ProjectOutputDirectory}",
            $"Project file count: {projectFiles.Count}",
            "",
            "Latest project files:",
            ..latestProjectFiles,
            "",
            "Developer code blocks can be written as:",
            "```file:src/MyProject/Program.cs",
            "// code",
            "```"
        ]);
    }

    public string CreateProjectFolderName(string idea)
    {
        var baseName = DetectProjectBaseName(idea);
        return $"{baseName}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}";
    }

    public async Task<IReadOnlyList<string>> WriteConsoleScaffoldAsync(string idea, string projectFolderName)
    {
        return IsDiceIdea(idea)
            ? await WriteDiceConsoleScaffoldAsync(idea, projectFolderName)
            : await WriteTodoConsoleScaffoldAsync(idea, projectFolderName);
    }

    private async Task<IReadOnlyList<string>> WriteTodoConsoleScaffoldAsync(string idea, string projectFolderName)
    {
        var files = new Dictionary<string, string>
        {
            [$"{projectFolderName}/TodoConsole.csproj"] = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """,
            [$"{projectFolderName}/Program.cs"] = $$"""
            using System.Text.Json;

            var store = new TodoStore(Path.Combine(AppContext.BaseDirectory, "tasks.json"));
            var tasks = await store.LoadAsync();

            Console.WriteLine("TodoConsole");
            Console.WriteLine("Idea: {{idea.Replace("\"", "'")}}");
            Console.WriteLine("Commands: add <text>, list, done <id>, delete <id>, exit");

            while (true)
            {
                Console.Write("> ");
                var input = Console.ReadLine()?.Trim() ?? "";
                if (input.Length == 0)
                {
                    continue;
                }

                var parts = input.Split(' ', 2, StringSplitOptions.TrimEntries);
                var command = parts[0].ToLowerInvariant();
                var argument = parts.Length > 1 ? parts[1] : "";

                switch (command)
                {
                    case "add":
                        if (string.IsNullOrWhiteSpace(argument))
                        {
                            Console.WriteLine("Usage: add <text>");
                            break;
                        }

                        var nextId = tasks.Count == 0 ? 1 : tasks.Max(task => task.Id) + 1;
                        tasks.Add(new TodoItem(nextId, argument, false));
                        await store.SaveAsync(tasks);
                        Console.WriteLine($"Added #{nextId}");
                        break;

                    case "list":
                        if (tasks.Count == 0)
                        {
                            Console.WriteLine("No tasks yet.");
                            break;
                        }

                        foreach (var task in tasks.OrderBy(task => task.Id))
                        {
                            Console.WriteLine($"{task.Id}. [{(task.IsCompleted ? "x" : " ")}] {task.Description}");
                        }
                        break;

                    case "done":
                        if (!int.TryParse(argument, out var doneId))
                        {
                            Console.WriteLine("Usage: done <id>");
                            break;
                        }

                        var doneTask = tasks.FirstOrDefault(task => task.Id == doneId);
                        if (doneTask is null)
                        {
                            Console.WriteLine($"Task not found: {doneId}");
                            break;
                        }

                        tasks[tasks.IndexOf(doneTask)] = doneTask with { IsCompleted = true };
                        await store.SaveAsync(tasks);
                        Console.WriteLine($"Completed #{doneId}");
                        break;

                    case "delete":
                        if (!int.TryParse(argument, out var deleteId))
                        {
                            Console.WriteLine("Usage: delete <id>");
                            break;
                        }

                        var removed = tasks.RemoveAll(task => task.Id == deleteId);
                        if (removed == 0)
                        {
                            Console.WriteLine($"Task not found: {deleteId}");
                            break;
                        }

                        await store.SaveAsync(tasks);
                        Console.WriteLine($"Deleted #{deleteId}");
                        break;

                    case "exit":
                        return;

                    default:
                        Console.WriteLine("Unknown command. Use: add <text>, list, done <id>, delete <id>, exit");
                        break;
                }
            }

            public sealed record TodoItem(int Id, string Description, bool IsCompleted);

            public sealed class TodoStore(string path)
            {
                private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

                public async Task<List<TodoItem>> LoadAsync()
                {
                    if (!File.Exists(path))
                    {
                        return [];
                    }

                    await using var stream = File.OpenRead(path);
                    return await JsonSerializer.DeserializeAsync<List<TodoItem>>(stream, JsonOptions) ?? [];
                }

                public async Task SaveAsync(List<TodoItem> tasks)
                {
                    await using var stream = File.Create(path);
                    await JsonSerializer.SerializeAsync(stream, tasks, JsonOptions);
                }
            }
            """,
            [$"{projectFolderName}/README.md"] = """
            # TodoConsole

            Generated fallback console todo project.

            ## Commands

            ```text
            add <text>
            list
            done <id>
            delete <id>
            exit
            ```

            Data is persisted to `tasks.json` next to the executable.
            """
        };

        return await WriteFilesAsync(files);
    }

    private async Task<IReadOnlyList<string>> WriteDiceConsoleScaffoldAsync(string idea, string projectFolderName)
    {
        var files = new Dictionary<string, string>
        {
            [$"{projectFolderName}/DiceConsole.csproj"] = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """,
            [$"{projectFolderName}/Program.cs"] = $$"""
            Console.WriteLine("DiceConsole");
            Console.WriteLine("Idea: {{idea.Replace("\"", "'")}}");
            Console.Write("Zar sayisi girin: ");

            var input = Console.ReadLine();
            if (!int.TryParse(input, out var diceCount) || diceCount <= 0)
            {
                Console.WriteLine("Gecerli pozitif bir zar sayisi girin.");
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine("Zar atildi!");
            for (var index = 1; index <= diceCount; index++)
            {
                var result = Random.Shared.Next(1, 7);
                Console.WriteLine($"Zar {index}: {result}");
            }
            """,
            [$"{projectFolderName}/README.md"] = """
            # DiceConsole

            Generated fallback console dice roller project.

            ## Behavior

            - Asks how many dice to roll.
            - Validates the input as a positive integer.
            - Prints one result between 1 and 6 for each die.
            """
        };

        return await WriteFilesAsync(files);
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

    private async Task<IReadOnlyList<string>> WriteFilesAsync(Dictionary<string, string> files)
    {
        var written = new List<string>();
        foreach (var (relativePath, content) in files)
        {
            var safePath = ResolveProjectOutputPath(relativePath);
            if (safePath is null)
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(safePath)!);
            await File.WriteAllTextAsync(safePath, NormalizeMultiline(content));
            written.Add(Path.GetRelativePath(paths.OutputsDirectory, safePath));
        }

        return written;
    }

    private static bool IsDiceIdea(string idea) =>
        idea.Contains("zar", StringComparison.OrdinalIgnoreCase) ||
        idea.Contains("dice", StringComparison.OrdinalIgnoreCase);

    private static string DetectProjectBaseName(string idea)
    {
        if (IsDiceIdea(idea))
        {
            return "DiceConsole";
        }

        if (idea.Contains("todo", StringComparison.OrdinalIgnoreCase) ||
            idea.Contains("yapılacak", StringComparison.OrdinalIgnoreCase) ||
            idea.Contains("yapilacak", StringComparison.OrdinalIgnoreCase))
        {
            return "TodoConsole";
        }

        return "GeneratedConsole";
    }

    private static bool IsSourceArtifact(string file)
    {
        var blockedSegments = new[]
        {
            $"{Path.DirectorySeparatorChar}.vs{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"
        };
        return !blockedSegments.Any(segment => file.Contains(segment, StringComparison.OrdinalIgnoreCase));
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

    private static string NormalizeMultiline(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var nonEmpty = lines.Where(line => line.Trim().Length > 0).ToArray();
        var indent = nonEmpty.Length == 0 ? 0 : nonEmpty.Min(line => line.TakeWhile(char.IsWhiteSpace).Count());
        return string.Join(Environment.NewLine, lines.Select(line => line.Length >= indent ? line[indent..] : line)).Trim() + Environment.NewLine;
    }

    private sealed record FileBlock(string RelativePath, string Content);
}
