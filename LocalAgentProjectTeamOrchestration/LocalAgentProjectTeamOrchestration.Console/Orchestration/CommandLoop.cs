using System.Text.Json;
using LocalAgentProjectTeamOrchestration.Evaluation;
using LocalAgentProjectTeamOrchestration.Observability;
using LocalAgentProjectTeamOrchestration.State;
using LocalAgentProjectTeamOrchestration.Storage;
using LocalAgentProjectTeamOrchestration.Tools;

namespace LocalAgentProjectTeamOrchestration.Orchestration;

internal sealed class CommandLoop(
    SessionState initialSession,
    SessionStore sessionStore,
    MemoryStore memoryStore,
    TeamOrchestrator orchestrator,
    StructuredAnalyzer analyzer,
    GraphRunner graphRunner,
    ToolRegistry toolRegistry,
    EvaluationHarness evaluator,
    ArtifactStore artifactStore,
    ConsoleRenderer console)
{
    private SessionState _session = initialSession;
    private CancellationTokenSource? _activeRunCts;
    private Task? _activeRun;

    public async Task RunAsync()
    {
        while (true)
        {
            System.Console.WriteLine();
            System.Console.Write("> ");
            var input = System.Console.ReadLine();
            if (input is null)
            {
                return;
            }

            var trimmed = input.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var command = trimmed.Split(' ', 2)[0].ToLowerInvariant();
            var argument = trimmed.Contains(' ') ? trimmed[(trimmed.IndexOf(' ') + 1)..].Trim() : "";

            try
            {
                switch (command)
                {
                    case "/idea":
                        await SetIdeaAsync(argument);
                        break;
                    case "/analyze":
                        _session = await analyzer.AnalyzeAsync(_session);
                        break;
                    case "/run":
                        await StartRunAsync(ct => orchestrator.RunTeamAsync(_session, ct));
                        break;
                    case "/implement":
                        _session = await orchestrator.ImplementAsync(_session, CancellationToken.None);
                        await sessionStore.SaveAsync(_session);
                        break;
                    case "/graph":
                        await StartRunAsync(ct => graphRunner.RunAsync(_session, ct));
                        break;
                    case "/tools":
                        await ShowToolsAsync();
                        break;
                    case "/state":
                        await ShowStateAsync();
                        break;
                    case "/history":
                        await ShowHistoryAsync();
                        break;
                    case "/outputs":
                        await ShowOutputsAsync();
                        break;
                    case "/eval":
                        await evaluator.RunAsync();
                        break;
                    case "/stop":
                        await StopAsync(waitForCompletion: false);
                        break;
                    case "/reset":
                        await ResetAsync();
                        break;
                    case "/exit":
                        await StopAsync(waitForCompletion: true);
                        return;
                    default:
                        await console.ErrorAsync($"Unknown command: {command}");
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                _session = _session with { IsCancelled = true };
                _session = _session.Touch("cancelled", "active run cancelled");
                await sessionStore.SaveAsync(_session);
                await console.SystemAsync("Active run cancelled.");
            }
            catch (Exception ex)
            {
                await console.ErrorAsync(ex.Message);
            }
        }
    }

    private async Task SetIdeaAsync(string idea)
    {
        if (string.IsNullOrWhiteSpace(idea))
        {
            await console.ErrorAsync("Usage: /idea <project or issue text>");
            return;
        }

        var message = new ConversationMessage(_session.NextSequence(), DateTimeOffset.Now, AgentRole.User, "human", idea);
        _session = _session.AddMessage(message, 24) with
        {
            CurrentIdea = idea,
            CurrentPhase = "idea-set",
            LastCompletedStep = "idea captured",
            IsCancelled = false
        };
        await sessionStore.SaveAsync(_session);
        await memoryStore.AppendDecisionAsync("Idea captured", idea);
        await console.UserAsync(idea);
    }

    private async Task StartRunAsync(Func<CancellationToken, Task<SessionState>> run)
    {
        if (_activeRun is { IsCompleted: false })
        {
            await console.ErrorAsync("A run is already active. Use /stop first.");
            return;
        }

        _activeRunCts = new CancellationTokenSource();
        var token = _activeRunCts.Token;
        _activeRun = Task.Run(async () =>
        {
            try
            {
                _session = await run(token);
                await sessionStore.SaveAsync(_session);
                await console.SystemAsync("Run finished. Use /state or /history to inspect persisted context.");
            }
            catch (OperationCanceledException)
            {
                _session = _session with { IsCancelled = true };
                _session = _session.Touch("cancelled", "active run cancelled");
                await sessionStore.SaveAsync(_session);
                await console.SystemAsync("Run cancelled and state was saved.");
            }
            finally
            {
                _activeRunCts?.Dispose();
                _activeRunCts = null;
            }
        }, CancellationToken.None);

        await console.SystemAsync("Run started in the background. Use /stop to cancel.");
    }

    private async Task StopAsync(bool waitForCompletion)
    {
        if (_activeRun is not { IsCompleted: false } || _activeRunCts is null)
        {
            await console.SystemAsync("No active run.");
            return;
        }

        _activeRunCts.Cancel();
        await console.SystemAsync("Stop requested.");
        if (waitForCompletion)
        {
            try
            {
                await _activeRun;
            }
            catch (OperationCanceledException)
            {
                // Background run records the cancelled state before completing.
            }
        }
    }

    private async Task ResetAsync()
    {
        await StopAsync(waitForCompletion: true);
        _session = await sessionStore.ArchiveAndResetAsync();
        await memoryStore.AppendDecisionAsync("Session reset", "Previous JSON state was archived. A fresh session was created.");
        await console.SystemAsync("State archived and reset.");
    }

    private async Task ShowToolsAsync()
    {
        var rows = toolRegistry.DescribeTools()
            .Select(tool => $"- {tool.Name} [{tool.Permission}]: {tool.Description}");
        await console.WriteBlockAsync("Tool registry and permission matrix", "local", string.Join(Environment.NewLine, rows), ConsoleColor.Magenta);
    }

    private async Task ShowStateAsync()
    {
        var json = JsonSerializer.Serialize(_session, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await console.WriteBlockAsync("Session state", "local", json, ConsoleColor.Magenta);
    }

    private async Task ShowHistoryAsync()
    {
        if (_session.Messages.Count == 0)
        {
            await console.SystemAsync("History is empty.");
            return;
        }

        var history = string.Join(Environment.NewLine + Environment.NewLine, _session.Messages.Select(m => $"### {m.Sequence}. {m.Role} ({m.Model}) - {m.Timestamp:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{m.Content}"));
        await console.WriteBlockAsync("Conversation history", "local", history, ConsoleColor.Magenta);
    }

    private async Task ShowOutputsAsync()
    {
        await console.WriteBlockAsync("Output folders", "local", artifactStore.Describe(), ConsoleColor.Magenta);
    }
}
