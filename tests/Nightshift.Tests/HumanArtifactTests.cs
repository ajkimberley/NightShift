using Nightshift.Core;

namespace Nightshift.Tests;

public class HumanArtifactTests
{
    private const string Human = "digraph G { start [shape=Mdiamond] review [shape=hexagon] accept [prompt=accept] revise [prompt=revise] exit [shape=Msquare] start -> review review -> accept [label=\"[A] Accept\"] review -> revise [label=\"R) Revise\"] revise -> review accept -> exit }";
    [Theory]
    [InlineData("A", true, 0)][InlineData("R,A", true, 1)][InlineData("SKIPPED", false, 0)][InlineData("TIMEOUT", false, 0)][InlineData("unknown", false, 0)]
    public async Task QueuedAcceptReviseSkipTimeoutAndUnknownAreDeterministic(string answers, bool success, int reviseVisits)
    {
        using var w = new TestWorkspace(); var recorder = new RecordingInterviewer(new QueueInterviewer(answers.Split(',').Select(a => new Answer(a, Identity: "test"))));
        var r = await new Engine(EngineTests.Registry(w.Root, interviewer: recorder), new NoWait()).RunAsync(Human, w.RunDirectory(), w.Root);
        Assert.Equal(success, r.Outcome.IsSuccess); Assert.Equal(reviseVisits, r.Checkpoint.NodeVisits.GetValueOrDefault("revise"));
        Assert.Equal(answers.Split(',').Length, recorder.Recordings.Count);
        Assert.NotEmpty(Directory.GetFiles(w.RunDirectory(), "interview.json", SearchOption.AllDirectories));
        if (success) Assert.Equal("test", r.Checkpoint.ContextValues["human.gate.identity"].GetString());
    }
    [Fact]
    public async Task HumanDefaultIsATargetAndTimeoutWithoutDefaultMayRetry()
    {
        using var w = new TestWorkspace();
        var r = await new Engine(EngineTests.Registry(w.Root, interviewer: new QueueInterviewer([new("TIMEOUT")]))).RunAsync(Human.Replace("shape=hexagon", "shape=hexagon, human.default_choice=accept"), w.RunDirectory(), w.Root);
        Assert.True(r.Outcome.IsSuccess);
        var retry = await new Engine(EngineTests.Registry(w.Root, interviewer: new QueueInterviewer([new("TIMEOUT"), new("A")])) , new NoWait()).RunAsync(Human.Replace("shape=hexagon", "shape=hexagon, max_retries=1"), w.RunDirectory("retry"), w.Root);
        Assert.True(retry.Outcome.IsSuccess); Assert.Equal(1, retry.Checkpoint.NodeVisits["review"]);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(w.RunDirectory("retry"), "review"), "interview.json", SearchOption.AllDirectories).Length);
    }
    [Fact]
    public async Task HumanTimeoutCancelsAsyncInterviewerAndExternalCancellationIsPreserved()
    {
        using var w = new TestWorkspace();
        var interviewer = new CallbackInterviewer(async (_, token) => { await Task.Delay(10000, token); return new("A"); });
        var engine = new Engine(EngineTests.Registry(w.Root, interviewer: interviewer));
        var r = await engine.RunAsync(Human.Replace("shape=hexagon", "shape=hexagon, human.timeout=20ms, human.default_choice=accept"), w.RunDirectory(), w.Root);
        Assert.True(r.Outcome.IsSuccess);
        using var cancel = new CancellationTokenSource(100);
        var stopped = await engine.RunAsync(Human, w.RunDirectory("cancel"), w.Root, cancellationToken: cancel.Token);
        Assert.Equal("interrupted", stopped.State);
    }
    [Fact]
    public async Task ConsoleQueueCallbackRecordingAndAskMultipleDoNotAutoApprove()
    {
        IInterviewer queue = new QueueInterviewer([new("NO"), new("text")]);
        var q = new Question("question", QuestionType.YesNo, [], "stage");
        var answers = await queue.AskMultipleAsync([q, q with { Type = QuestionType.Freeform }], CancellationToken.None);
        Assert.Equal(new[] { "NO", "text" }, answers.Select(a => a.Value));
        Assert.Equal("SKIPPED", (await queue.AskAsync(q, CancellationToken.None)).Value);
        var auto = new AutoApproveInterviewer(); Assert.Equal("YES", (await auto.AskAsync(q, CancellationToken.None)).Value);
        var prior = Console.In;
        try
        {
            Console.SetIn(new StringReader("yes\nno\nfree text\n")); var console = new ConsoleInterviewer();
            Assert.Equal("YES", (await console.AskAsync(q, CancellationToken.None)).Value);
            Assert.Equal("NO", (await console.AskAsync(q with { Type = QuestionType.Confirmation }, CancellationToken.None)).Value);
            Assert.Equal("free text", (await console.AskAsync(q with { Type = QuestionType.Freeform }, CancellationToken.None)).Text);
            Assert.Equal("auto-approved", (await auto.AskAsync(q with { Type = QuestionType.Freeform }, CancellationToken.None)).Text);
            var choices = q with { Type = QuestionType.MultipleChoice, Options = [new("A", "first", "a"), new("B", "second", "b")] };
            Assert.Equal("A", (await auto.AskAsync(choices, CancellationToken.None)).Value);
        }
        finally { Console.SetIn(prior); }
    }
    [Fact]
    public void ContextDeepCloneAndArtifactThresholdStorageWork()
    {
        var c = new RunContext(); c.Set("nested", new { items = new[] { 1, 2 } }); var clone = c.Clone(); clone.Set("nested", "changed"); Assert.Contains("items", c.Get("nested"));
        using var w = new TestWorkspace(); var store = new ArtifactStore(w.Root, 20);
        Assert.False(store.Store("small", "small", 3).IsFileBacked); Assert.True(store.Store("large", "large", new string('x', 100)).IsFileBacked);
        Assert.Equal(new string('x', 100), store.Retrieve<string>("large")); Assert.Equal(2, store.List().Count);
        Assert.Throws<ArgumentException>(() => store.Store("../escape", "bad", 1));
        store.Remove("large"); Assert.False(store.Has("large")); Assert.Empty(Directory.GetFiles(Path.Combine(w.Root, "artifacts"))); store.Clear(); Assert.Empty(store.List());
    }
    [Fact]
    public async Task AtomicWritesLeaveValidJsonAndNoTemporaryFiles()
    {
        using var w = new TestWorkspace(); string path = Path.Combine(w.Root, "state.json");
        await AtomicFile.WriteJsonAsync(path, new { version = 1 }); await AtomicFile.WriteJsonAsync(path, new { version = 2 });
        Assert.Equal(2, System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(path)).RootElement.GetProperty("version").GetInt32());
        Assert.Empty(Directory.GetFiles(w.Root, "*.tmp"));
    }
}
