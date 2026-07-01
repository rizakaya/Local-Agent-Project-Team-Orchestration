using LocalAgentProjectTeamOrchestration.Agents;
using LocalAgentProjectTeamOrchestration.Observability;
using LocalAgentProjectTeamOrchestration.Security;
using LocalAgentProjectTeamOrchestration.State;
using LocalAgentProjectTeamOrchestration.Storage;

namespace LocalAgentProjectTeamOrchestration.Orchestration;

internal sealed class TeamOrchestrator(
    AgentFactory agentFactory,
    OllamaAgentClient ollama,
    SessionStore sessionStore,
    MemoryStore memoryStore,
    ArtifactStore artifactStore,
    ConsoleRenderer console,
    JsonlTraceWriter traceWriter,
    GuardrailService guardrails)
{
    public async Task<SessionState> RunTeamAsync(SessionState session, CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("N");
        await traceWriter.WriteAsync(new AgentRun(runId, DateTimeOffset.Now, "/run", session.CurrentIdea));
        var inputCheck = guardrails.CheckInput(session.CurrentIdea);
        if (!inputCheck.IsAllowed)
        {
            await console.ErrorAsync(inputCheck.Reason, runId);
            return session;
        }

        var memory = await memoryStore.ReadAsync();
        var current = session with { IsCancelled = false };

        current = await CallAndStoreAsync(current, runId, AgentRole.Lead, "Restate the goal, ask Analyst for a requirements document, and do not implement code.", memory, cancellationToken);
        current = await CallAndStoreAsync(current, runId, AgentRole.Analyst, "Create the requirements document for the current idea.", memory, cancellationToken);
        current = await CallAndStoreAsync(current, runId, AgentRole.Developer, "Create a .NET implementation approach from the accepted requirements. Do not broaden scope. If you produce project code, emit each file as a fenced block that starts with ```file:relative/path under outputs/project.", memory, cancellationToken, current.AnalystOutput);
        current = await CallAndStoreAsync(current, runId, AgentRole.Tester, "Create a test plan from the requirements and developer approach.", memory, cancellationToken, current.AnalystOutput, current.DeveloperOutput);
        current = await CallAndStoreAsync(current, runId, AgentRole.Lead, "Review all role outputs and produce the final decision and next actionable step.", memory, cancellationToken, current.AnalystOutput, current.DeveloperOutput, current.TesterOutput);

        await memoryStore.AppendDecisionAsync("Latest team run", current.LeadOutput);
        return current.Touch("team-run-complete", "Lead final decision");
    }

    private async Task<SessionState> CallAndStoreAsync(SessionState session, string runId, AgentRole role, string instruction, string memory, CancellationToken cancellationToken, params string[] artifacts)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var agent = agentFactory.Create(role);
        var messages = PromptBuilder.Build(instruction, session.CurrentIdea, memory, session.Messages, artifacts);
        var promptText = string.Join(Environment.NewLine, messages.Select(m => m.Content));
        var budgetCheck = guardrails.CheckPromptBudget(promptText);
        if (!budgetCheck.IsAllowed)
        {
            await console.ErrorAsync(budgetCheck.Reason, runId);
            return session.Touch("blocked", $"{role} prompt budget");
        }

        await traceWriter.WriteAsync(new AgentStep(runId, Guid.NewGuid().ToString("N"), DateTimeOffset.Now, role.ToString(), agent.Model, "started", instruction));
        string output;
        try
        {
            output = await ollama.ChatAsync(agent, messages, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            output = $"Ollama error for {role} ({agent.Model}): {ex.Message}";
            await console.ErrorAsync(output, runId);
        }

        var outputCheck = guardrails.CheckOutput(output);
        if (!outputCheck.IsAllowed)
        {
            output = outputCheck.Reason;
        }

        var conversation = new ConversationMessage(session.NextSequence(), DateTimeOffset.Now, role, agent.Model, output);
        await console.AgentAsync(conversation);
        await artifactStore.SaveRoleOutputAsync(role, output);
        if (role == AgentRole.Developer)
        {
            var writtenFiles = await artifactStore.SaveDeveloperCodeBlocksAsync(output);
            if (writtenFiles.Count > 0)
            {
                await console.SystemAsync($"Developer code artifacts written: {string.Join(", ", writtenFiles)}");
            }
        }
        var next = session.AddMessage(conversation, 24).Touch("team-run", $"{role} completed");
        next = role switch
        {
            AgentRole.Analyst => next with { AnalystOutput = output },
            AgentRole.Developer => next with { DeveloperOutput = output },
            AgentRole.Tester => next with { TesterOutput = output },
            AgentRole.Lead => next with { LeadOutput = output },
            _ => next
        };

        await sessionStore.SaveAsync(next);
        await traceWriter.WriteAsync(new AgentStep(runId, Guid.NewGuid().ToString("N"), DateTimeOffset.Now, role.ToString(), agent.Model, "completed", Trim(output, 400)));
        return next;
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..max] + "...";
}
