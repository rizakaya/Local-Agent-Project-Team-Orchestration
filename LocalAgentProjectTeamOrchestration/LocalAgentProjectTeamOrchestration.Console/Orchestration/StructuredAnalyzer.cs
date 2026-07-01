using System.Text.Json;
using LocalAgentProjectTeamOrchestration.Observability;
using LocalAgentProjectTeamOrchestration.State;
using LocalAgentProjectTeamOrchestration.Storage;

namespace LocalAgentProjectTeamOrchestration.Orchestration;

internal sealed class StructuredAnalyzer(SessionStore sessionStore, ConsoleRenderer console, JsonlTraceWriter traceWriter)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<SessionState> AnalyzeAsync(SessionState session)
    {
        if (string.IsNullOrWhiteSpace(session.CurrentIdea))
        {
            await console.ErrorAsync("Set an idea first with /idea <text>.");
            return session;
        }

        var analysis = new TaskAnalysis(
            TaskType: session.CurrentIdea.Contains("issue", StringComparison.OrdinalIgnoreCase) ? "IssueAnalysis" : "ProjectIdea",
            Goal: session.CurrentIdea,
            Inputs: ["user idea", "project skills", "persistent memory"],
            Constraints: ["local models", "console application", "JSON/Markdown persistence", "no external NuGet packages"],
            RequiredTools: ["ReadFile", "SearchCode", "RunTests"],
            Risks: ["Ollama may be offline", "requirements may be ambiguous", "large model responses may exceed context budget"],
            AcceptanceCriteria: ["TaskAnalysis JSON is valid", "state file is updated", "next orchestration step is clear"],
            EstimatedDifficulty: 3);

        var json = JsonSerializer.Serialize(analysis, JsonOptions);
        var status = analysis.IsValid(out var error) ? "valid" : $"invalid: {error}";
        await console.WriteBlockAsync("TaskAnalysis structured output", "local", $"{json}{Environment.NewLine}{Environment.NewLine}Validation: {status}", ConsoleColor.Magenta);
        var next = session with { LastTaskAnalysis = analysis };
        next = next.Touch("analyzed", "TaskAnalysis generated");
        await sessionStore.SaveAsync(next);
        await traceWriter.WriteAsync(new AgentStep(Guid.NewGuid().ToString("N"), "structured-output", DateTimeOffset.Now, "StructuredAnalyzer", "local", status, analysis.Goal));
        return next;
    }
}

internal sealed class GraphRunner(SessionStore sessionStore, ConsoleRenderer console, JsonlTraceWriter traceWriter, Security.GuardrailService guardrails)
{
    public async Task<SessionState> RunAsync(SessionState session, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.CurrentIdea))
        {
            await console.ErrorAsync("Set an idea first with /idea <text>.");
            return session;
        }

        var runId = Guid.NewGuid().ToString("N");
        await traceWriter.WriteAsync(new AgentRun(runId, DateTimeOffset.Now, "/graph", session.CurrentIdea));
        var state = session.GraphState with { Stage = GraphStage.ParseIssue };
        await StepAsync(runId, "ParseIssue", "Create structured TaskAnalysis.");

        var analysis = session.LastTaskAnalysis ?? new TaskAnalysis(
            "IssueAnalysis",
            session.CurrentIdea,
            ["idea", "state", "memory"],
            ["local files", "guardrails"],
            ["ReadFile", "SearchCode"],
            ["unclear scope"],
            ["final report exists"],
            3);
        state = state with { ParsedTask = analysis, Stage = GraphStage.AnalyzeImpact };
        cancellationToken.ThrowIfCancellationRequested();

        await StepAsync(runId, "AnalyzeImpact", "Estimate affected components and learning topics.");
        state = state with
        {
            ImpactAnalysis = "Likely impacts: agent prompts, state persistence, tool permissions, trace logging, and README examples.",
            Stage = GraphStage.RiskReview
        };
        cancellationToken.ThrowIfCancellationRequested();

        await StepAsync(runId, "RiskReview", "Apply guardrails and identify risks.");
        var guardrail = guardrails.CheckInput(session.CurrentIdea);
        state = state with
        {
            RiskReview = guardrail.IsAllowed
                ? "Input guardrail passed. Main risks are Ollama availability and ambiguous requirements."
                : $"Input guardrail blocked: {guardrail.Reason}",
            Stage = GraphStage.FinalReport
        };
        cancellationToken.ThrowIfCancellationRequested();

        await StepAsync(runId, "FinalReport", "Create final stateful report.");
        state = state with
        {
            FinalReport = $"Graph completed for '{session.CurrentIdea}'. Next step: run /run for multi-agent discussion or inspect /state.",
            Stage = GraphStage.Completed
        };

        var next = session with { GraphState = state };
        next = next.Touch("graph-complete", "FinalReport");
        await sessionStore.SaveAsync(next);
        await console.WriteBlockAsync("Graph Result", "local", state.FinalReport, ConsoleColor.Magenta);
        return next;

        async Task StepAsync(string id, string step, string summary)
        {
            await traceWriter.WriteAsync(new AgentStep(id, step, DateTimeOffset.Now, "GraphRunner", "local", "completed", summary));
            await console.WriteBlockAsync($"Graph: {step}", "local", summary, ConsoleColor.DarkCyan);
        }
    }
}
