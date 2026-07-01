using System.Net.Http.Json;
using System.Text.Json;
using LocalAgentProjectTeamOrchestration.State;

namespace LocalAgentProjectTeamOrchestration.Agents;

internal sealed record AgentDefinition(AgentRole Role, string Name, string Model, string SystemPrompt);
internal sealed record ChatMessage(string Role, string Content);

internal sealed class AgentFactory(AppPaths paths, AppConfig config)
{
    public AgentDefinition Create(AgentRole role)
    {
        var (skillName, model) = role switch
        {
            AgentRole.Lead => ("project-lead", config.Models.Lead),
            AgentRole.Analyst => ("project-analyst", config.Models.Analyst),
            AgentRole.Developer => ("project-developer", config.Models.Developer),
            AgentRole.Tester => ("project-tester", config.Models.Tester),
            _ => throw new InvalidOperationException($"No agent definition exists for role {role}.")
        };

        var skillRoot = paths.ResolveFromSolution(config.Skills.Root);
        var skillPath = Path.Combine(skillRoot, skillName, "SKILL.md");
        var protocolPath = Path.Combine(skillRoot, skillName, "references", "team-protocol.md");
        var skill = File.Exists(skillPath) ? File.ReadAllText(skillPath) : $"# {skillName}";
        var protocol = File.Exists(protocolPath) ? File.ReadAllText(protocolPath) : "";
        var prompt = $"""
        You are {role} in a local-model software project team.
        Follow this skill exactly.

        {skill}

        Team protocol:
        {protocol}
        """;

        return new AgentDefinition(role, role.ToString(), model, prompt);
    }

    public AgentDefinition CreateImplementationDeveloper()
    {
        var developer = Create(AgentRole.Developer);
        var implementationPrompt = $"""
        {developer.SystemPrompt}

        Implementation mode override:
        The normal Developer Plan output format is disabled for this request.
        You must generate real project files only.
        Output only fenced file blocks starting with ```file:relative/path.
        Do not include prose, analysis, summaries, or markdown sections outside file blocks.
        """;

        return developer with { SystemPrompt = implementationPrompt };
    }
}

internal sealed class OllamaAgentClient(HttpClient httpClient, string baseUrl)
{
    private readonly Uri _chatUri = new(new Uri(baseUrl.TrimEnd('/') + "/"), "api/chat");

    public async Task<string> ChatAsync(AgentDefinition agent, IEnumerable<ChatMessage> messages, CancellationToken cancellationToken)
    {
        var request = new OllamaChatRequest(
            agent.Model,
            messages.Prepend(new ChatMessage("system", agent.SystemPrompt)).Select(m => new OllamaMessage(m.Role, m.Content)).ToArray(),
            Stream: false);

        using var response = await httpClient.PostAsJsonAsync(_chatUri, request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Ollama returned {(int)response.StatusCode}: {body}");
        }

        var result = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: cancellationToken);
        return result?.Message?.Content?.Trim() ?? "";
    }

    private sealed record OllamaChatRequest(string Model, OllamaMessage[] Messages, bool Stream);
    private sealed record OllamaMessage(string Role, string Content);
    private sealed record OllamaChatResponse(OllamaMessage? Message);
}

internal static class PromptBuilder
{
    public static List<ChatMessage> Build(string roleInstruction, string idea, string memory, IEnumerable<ConversationMessage> recentMessages, params string[] artifacts)
    {
        var recent = string.Join(Environment.NewLine, recentMessages.Select(m => $"- {m.Role}: {Trim(m.Content, 700)}"));
        var artifactText = string.Join(Environment.NewLine + Environment.NewLine, artifacts.Where(a => !string.IsNullOrWhiteSpace(a)));
        return
        [
            new("user", $"""
            Role request:
            {roleInstruction}

            Current idea:
            {idea}

            Persistent memory:
            {Trim(memory, 3000)}

            Recent context:
            {recent}

            Artifacts:
            {artifactText}
            """)
        ];
    }

    public static List<ChatMessage> BuildImplementation(string roleInstruction, string idea, string memory, params string[] artifacts)
    {
        var artifactText = string.Join(Environment.NewLine + Environment.NewLine, artifacts.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => Trim(a, 5000)));
        return
        [
            new("user", $"""
            Implementation request:
            {roleInstruction}

            Current idea:
            {idea}

            Short memory:
            {Trim(memory, 1200)}

            Required source artifacts:
            {artifactText}
            """)
        ];
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..max] + "...";
}
