namespace Nightshift.Core;

public enum QuestionType { YesNo, MultipleChoice, Freeform, Confirmation }
public sealed record Option(string Key, string Label, string Target);
public sealed record Question(string Text, QuestionType Type, IReadOnlyList<Option> Options, string Stage, TimeSpan? Timeout = null, string? Default = null);
public sealed record Answer(string Value, string Text = "", string Identity = "unverified");
public interface IInterviewer
{
    Task<Answer> AskAsync(Question question, CancellationToken cancellationToken);
    async Task<IReadOnlyList<Answer>> AskMultipleAsync(IEnumerable<Question> questions, CancellationToken cancellationToken)
    {
        var answers = new List<Answer>(); foreach (var q in questions) answers.Add(await AskAsync(q, cancellationToken)); return answers;
    }
    void Inform(string message, string stage) { }
}
public sealed class QueueInterviewer(IEnumerable<Answer> answers) : IInterviewer
{
    private readonly Queue<Answer> queue = new(answers);
    public Task<Answer> AskAsync(Question question, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(queue.TryDequeue(out var a) ? a : new Answer("SKIPPED"));
    }
}
public sealed class CallbackInterviewer(Func<Question, CancellationToken, Task<Answer>> callback) : IInterviewer
{
    public Task<Answer> AskAsync(Question question, CancellationToken cancellationToken) => callback(question, cancellationToken);
}
public sealed class AutoApproveInterviewer : IInterviewer
{
    public Task<Answer> AskAsync(Question q, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(q.Type switch
        {
            QuestionType.MultipleChoice when q.Options.Count > 0 => new Answer(q.Options[0].Key, Identity: "auto-approver-test-only"),
            QuestionType.YesNo or QuestionType.Confirmation => new Answer("YES", Identity: "auto-approver-test-only"),
            _ => new Answer("auto-approved", "auto-approved", "auto-approver-test-only")
        });
    }
}
public sealed class ConsoleInterviewer : IInterviewer
{
    public async Task<Answer> AskAsync(Question question, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[{question.Stage}] {question.Text}"); foreach (var option in question.Options) Console.WriteLine($"  {option.Key}: {option.Label}");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (question.Timeout != null) timeout.CancelAfter(question.Timeout.Value);
        try
        {
            string? input = await Console.In.ReadLineAsync(timeout.Token);
            if (input == null) return new("SKIPPED", Identity: "local-console-unverified");
            string value = question.Type is QuestionType.YesNo or QuestionType.Confirmation ? input.Trim().ToLowerInvariant() switch
            {
                "y" or "yes" => "YES", "n" or "no" => "NO", _ => input
            } : input;
            return new(value, question.Type == QuestionType.Freeform ? input : "", "local-console-unverified");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new("TIMEOUT"); }
    }
}
public sealed record InterviewRecord(Question Question, Answer Answer, DateTimeOffset Timestamp);
public sealed class RecordingInterviewer(IInterviewer inner) : IInterviewer
{
    public List<InterviewRecord> Recordings { get; } = [];
    public async Task<Answer> AskAsync(Question question, CancellationToken cancellationToken)
    {
        var answer = await inner.AskAsync(question, cancellationToken); Recordings.Add(new(question, answer, DateTimeOffset.UtcNow)); return answer;
    }
    public void Inform(string message, string stage) => inner.Inform(message, stage);
}
