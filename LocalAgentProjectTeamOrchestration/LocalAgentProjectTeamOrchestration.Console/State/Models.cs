namespace LocalAgentProjectTeamOrchestration.State;

internal enum AgentRole
{
    Lead,
    Analyst,
    Developer,
    Tester,
    System,
    User
}

internal enum GraphStage
{
    NotStarted,
    ParseIssue,
    AnalyzeImpact,
    RiskReview,
    FinalReport,
    Completed,
    Cancelled,
    Failed
}

internal sealed record ConversationMessage(
    int Sequence,
    DateTimeOffset Timestamp,
    AgentRole Role,
    string Model,
    string Content);

internal sealed record TaskAnalysis(
    string TaskType,
    string Goal,
    string[] Inputs,
    string[] Constraints,
    string[] RequiredTools,
    string[] Risks,
    string[] AcceptanceCriteria,
    int EstimatedDifficulty)
{
    public bool IsValid(out string error)
    {
        if (string.IsNullOrWhiteSpace(Goal))
        {
            error = "Goal is required.";
            return false;
        }

        if (AcceptanceCriteria.Length == 0)
        {
            error = "At least one acceptance criterion is required.";
            return false;
        }

        if (EstimatedDifficulty is < 1 or > 5)
        {
            error = "EstimatedDifficulty must be between 1 and 5.";
            return false;
        }

        error = "";
        return true;
    }
}

internal sealed record IssueAnalysisState
{
    public GraphStage Stage { get; init; } = GraphStage.NotStarted;
    public TaskAnalysis? ParsedTask { get; init; }
    public string ImpactAnalysis { get; init; } = "";
    public string RiskReview { get; init; } = "";
    public string FinalReport { get; init; } = "";
}

internal sealed record SessionState
{
    public string SessionId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;
    public string CurrentIdea { get; init; } = "";
    public string CurrentPhase { get; init; } = "new";
    public string LastCompletedStep { get; init; } = "";
    public bool IsCancelled { get; init; }
    public TaskAnalysis? LastTaskAnalysis { get; init; }
    public string AnalystOutput { get; init; } = "";
    public string DeveloperOutput { get; init; } = "";
    public string TesterOutput { get; init; } = "";
    public string LeadOutput { get; init; } = "";
    public string LastProjectOutputDirectory { get; init; } = "";
    public IssueAnalysisState GraphState { get; init; } = new();
    public List<ConversationMessage> Messages { get; init; } = [];

    public SessionState Touch(string phase, string step) => this with
    {
        CurrentPhase = phase,
        LastCompletedStep = step,
        UpdatedAt = DateTimeOffset.Now
    };

    public SessionState AddMessage(ConversationMessage message, int recentLimit)
    {
        var messages = Messages.Concat([message]).TakeLast(Math.Max(1, recentLimit)).ToList();
        return this with { Messages = messages, UpdatedAt = DateTimeOffset.Now };
    }

    public int NextSequence() => Messages.Count == 0 ? 1 : Messages.Max(m => m.Sequence) + 1;
}
