using System.Text.Json;

namespace LocalAgentProjectTeamOrchestration;

internal sealed record AppConfig(
    OllamaConfig Ollama,
    ModelConfig Models,
    SkillConfig Skills,
    MemoryConfig Memory,
    SecurityConfig Security,
    GuardrailConfig Guardrails)
{
    public static AppConfig Load(AppPaths paths)
    {
        var configPath = Path.Combine(paths.AppBaseDirectory, "appsettings.json");
        if (!File.Exists(configPath))
        {
            configPath = Path.Combine(paths.ProjectDirectory, "appsettings.json");
        }

        if (!File.Exists(configPath))
        {
            return Defaults();
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<AppConfig>(json, options) ?? Defaults();
    }

    private static AppConfig Defaults() => new(
        new OllamaConfig("http://localhost:11434"),
        new ModelConfig("qwen3.6:latest", "qwen3.6:latest", "qwen3-coder:30b", "gemma3:12b"),
        new SkillConfig(@"..\.skills\skills"),
        new MemoryConfig(24),
        new SecurityConfig([".", "outputs", @"..\.skills\skills"], ["api_key", "apikey", "secret", "password", "token", "bearer "], ["dotnet build", "dotnet test", "dotnet run"]),
        new GuardrailConfig(12000, 20000, 50000));
}

internal sealed record OllamaConfig(string BaseUrl);
internal sealed record ModelConfig(string Lead, string Analyst, string Developer, string Tester);
internal sealed record SkillConfig(string Root);
internal sealed record MemoryConfig(int RecentMessageLimit);
internal sealed record SecurityConfig(string[] AllowedPaths, string[] SecretBlocklist, string[] CommandWhitelist);
internal sealed record GuardrailConfig(int MaxInputCharacters, int MaxOutputCharacters, int MaxEstimatedPromptCharacters);

internal sealed class AppPaths
{
    private AppPaths(string appBaseDirectory, string solutionDirectory, string workspaceDirectory, string projectDirectory)
    {
        AppBaseDirectory = appBaseDirectory;
        SolutionDirectory = solutionDirectory;
        WorkspaceDirectory = workspaceDirectory;
        ProjectDirectory = projectDirectory;
        DataDirectory = Path.Combine(SolutionDirectory, "data");
        ArchiveDirectory = Path.Combine(DataDirectory, "archive");
        LogsDirectory = Path.Combine(SolutionDirectory, "logs");
        OutputsDirectory = Path.Combine(SolutionDirectory, "outputs");
        AnalysisOutputDirectory = Path.Combine(OutputsDirectory, "analysis");
        DeveloperOutputDirectory = Path.Combine(OutputsDirectory, "developer");
        TesterOutputDirectory = Path.Combine(OutputsDirectory, "tester");
        LeadOutputDirectory = Path.Combine(OutputsDirectory, "lead");
        ProjectOutputDirectory = Path.Combine(OutputsDirectory, "project");
        SessionStatePath = Path.Combine(DataDirectory, "session-state.json");
        MemoryPath = Path.Combine(DataDirectory, "memory.md");
    }

    public string AppBaseDirectory { get; }
    public string SolutionDirectory { get; }
    public string WorkspaceDirectory { get; }
    public string ProjectDirectory { get; }
    public string DataDirectory { get; }
    public string ArchiveDirectory { get; }
    public string LogsDirectory { get; }
    public string OutputsDirectory { get; }
    public string AnalysisOutputDirectory { get; }
    public string DeveloperOutputDirectory { get; }
    public string TesterOutputDirectory { get; }
    public string LeadOutputDirectory { get; }
    public string ProjectOutputDirectory { get; }
    public string SessionStatePath { get; }
    public string MemoryPath { get; }

    public static AppPaths Discover()
    {
        var appBase = AppContext.BaseDirectory;
        var solution = FindAncestor(appBase, "LocalAgentProjectTeamOrchestration.slnx")
            ?? FindAncestor(Directory.GetCurrentDirectory(), "LocalAgentProjectTeamOrchestration.slnx")
            ?? Directory.GetCurrentDirectory();
        var project = FindAncestor(appBase, "LocalAgentProjectTeamOrchestration.Console.csproj")
            ?? Path.Combine(solution, "LocalAgentProjectTeamOrchestration.Console");
        var workspace = Directory.GetParent(solution)?.FullName ?? solution;
        return new AppPaths(appBase, solution, workspace, project);
    }

    public string ResolveFromSolution(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(SolutionDirectory, path));
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ArchiveDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(AnalysisOutputDirectory);
        Directory.CreateDirectory(DeveloperOutputDirectory);
        Directory.CreateDirectory(TesterOutputDirectory);
        Directory.CreateDirectory(LeadOutputDirectory);
        Directory.CreateDirectory(ProjectOutputDirectory);
    }

    private static string? FindAncestor(string start, string markerFile)
    {
        var current = new DirectoryInfo(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, markerFile)))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
