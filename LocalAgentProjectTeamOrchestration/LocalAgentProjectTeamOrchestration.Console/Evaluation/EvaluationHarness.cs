using System.Text.Json;
using LocalAgentProjectTeamOrchestration.Observability;

namespace LocalAgentProjectTeamOrchestration.Evaluation;

internal sealed record GoldenTask(string Id, string Prompt, string ExpectedTopic);
internal sealed record EvaluationResult(string TaskId, bool Passed, string Reason);

internal sealed class EvaluationHarness(AppPaths paths, ConsoleRenderer console, JsonlTraceWriter traceWriter)
{
    public async Task RunAsync()
    {
        var tasks = await LoadTasksAsync();
        var results = tasks.Select(task =>
        {
            var passed = task.Prompt.Contains(task.ExpectedTopic, StringComparison.OrdinalIgnoreCase);
            return new EvaluationResult(task.Id, passed, passed ? "keyword matched" : $"missing {task.ExpectedTopic}");
        }).ToList();

        var passedCount = results.Count(r => r.Passed);
        var body = string.Join(Environment.NewLine, results.Select(r => $"- {r.TaskId}: {(r.Passed ? "PASS" : "FAIL")} - {r.Reason}"));
        await console.WriteBlockAsync("Evaluation summary", "local", $"Score: {passedCount}/{results.Count}{Environment.NewLine}{body}", ConsoleColor.Magenta);
        await traceWriter.WriteAsync(new AgentStep(Guid.NewGuid().ToString("N"), "evaluation", DateTimeOffset.Now, "EvaluationHarness", "local", "completed", $"Score {passedCount}/{results.Count}"));
    }

    private async Task<List<GoldenTask>> LoadTasksAsync()
    {
        var source = Path.Combine(paths.ProjectDirectory, "Evaluation", "golden-tasks.json");
        if (!File.Exists(source))
        {
            source = Path.Combine(paths.AppBaseDirectory, "Evaluation", "golden-tasks.json");
        }

        if (!File.Exists(source))
        {
            return [];
        }

        await using var stream = File.OpenRead(source);
        return await JsonSerializer.DeserializeAsync<List<GoldenTask>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }
}
