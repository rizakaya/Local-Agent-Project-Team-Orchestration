using LocalAgentProjectTeamOrchestration.Agents;
using LocalAgentProjectTeamOrchestration.Evaluation;
using LocalAgentProjectTeamOrchestration.Observability;
using LocalAgentProjectTeamOrchestration.Orchestration;
using LocalAgentProjectTeamOrchestration.Security;
using LocalAgentProjectTeamOrchestration.State;
using LocalAgentProjectTeamOrchestration.Storage;
using LocalAgentProjectTeamOrchestration.Tools;

namespace LocalAgentProjectTeamOrchestration;

internal static class Program
{
    private static async Task Main()
    {
        var paths = AppPaths.Discover();
        var config = AppConfig.Load(paths);
        paths.EnsureDirectories();

        var sessionStore = new SessionStore(paths);
        var memoryStore = new MemoryStore(paths);
        var artifactStore = new ArtifactStore(paths);
        var session = await sessionStore.LoadAsync();
        await memoryStore.EnsureExistsAsync();

        var markdownLogger = new MarkdownConversationLogger(paths);
        var traceWriter = new JsonlTraceWriter(paths);
        var console = new ConsoleRenderer(markdownLogger, traceWriter);
        var permissionPolicy = new ToolPermissionPolicy(config.Security);
        var guardrails = new GuardrailService(config.Guardrails);
        var toolRegistry = ToolRegistry.CreateDefault(paths, permissionPolicy, traceWriter);
        var agentFactory = new AgentFactory(paths, config);
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var ollama = new OllamaAgentClient(httpClient, config.Ollama.BaseUrl);
        var orchestrator = new TeamOrchestrator(
            agentFactory,
            ollama,
            sessionStore,
            memoryStore,
            artifactStore,
            console,
            traceWriter,
            guardrails);
        var analyzer = new StructuredAnalyzer(sessionStore, console, traceWriter);
        var graphRunner = new GraphRunner(sessionStore, console, traceWriter, guardrails);
        var evaluator = new EvaluationHarness(paths, console, traceWriter);

        await console.SystemAsync("Local Agent Project Team Orchestration");
        await console.SystemAsync("Commands: /idea <text>, /analyze, /run, /graph, /tools, /outputs, /state, /history, /eval, /stop, /reset, /exit");
        await console.SystemAsync($"State: {paths.SessionStatePath}");

        var loop = new CommandLoop(
            session,
            sessionStore,
            memoryStore,
            orchestrator,
            analyzer,
            graphRunner,
            toolRegistry,
            evaluator,
            artifactStore,
            console);

        await loop.RunAsync();
    }
}
