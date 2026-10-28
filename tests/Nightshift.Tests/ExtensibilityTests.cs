using Nightshift.Core;

namespace Nightshift.Tests;

public class ExtensibilityTests
{
    private sealed class Transform(string value) : IGraphTransform
    {
        public Graph Apply(Graph graph) { graph.Nodes["work"].Attributes.Set("label", graph.Nodes["work"].Label + value); return graph; }
    }
    private sealed class Rule : ILintRule
    {
        public IEnumerable<Diagnostic> Apply(Graph graph) => [new("custom", Severity.Info, graph.Id, Fix: "example")];
    }
    [Fact]
    public void CustomTransformsRunInOrderAndCustomDiagnosticsPreserveSeverity()
    {
        string source = EngineTests.StageGraph(); var p = Preparation.Prepare(source, transforms: [new Transform("1"), new Transform("2")]);
        Assert.Equal("work12", p.Graph.Nodes["work"].Label); Assert.Equal("work", new DotParser().Parse(source).Nodes["work"].Label);
        Assert.Contains(Validation.Validate(p.Graph, extraRules: [new Rule()]), d => d.Rule == "custom" && d.Severity == Severity.Info && d.Fix == "example");
    }
    [Fact]
    public void BackoffCapsAndJittersAndSupportsConstantDelay()
    {
        Assert.Equal(100, new Backoff(100, 1, 1000, false).Delay(9).TotalMilliseconds);
        Assert.Equal(500, new Backoff(100, 2, 500, false).Delay(10).TotalMilliseconds);
        var samples = Enumerable.Range(0, 30).Select(_ => new Backoff(100, 2, 1000, true).Delay(1).TotalMilliseconds).ToList();
        Assert.All(samples, n => Assert.InRange(n, 50, 150)); Assert.True(samples.Distinct().Count() > 1);
    }
    [Theory]
    [InlineData("component")][InlineData("tripleoctagon")][InlineData("house")]
    public void DeferredHandlersAreRejectedInsteadOfReturningPlaceholderSuccess(string shape)
    {
        var p = Preparation.Prepare(EngineTests.StageGraph($", shape={shape}"));
        Assert.Contains(p.Diagnostics, d => d.Rule == "unsupported_handler" && d.Severity == Severity.Error);
    }
    [Fact]
    public void UnsupportedHooksAndLoopRestartAreExecutionErrors()
    {
        var p = Preparation.Prepare(EngineTests.StageGraph(", tool_hooks.pre=echo", "work -> exit [loop_restart=true]"));
        Assert.Equal(2, p.Diagnostics.Count(d => d.Rule == "unsupported_feature"));
    }
}
