using Nightshift.Core;

namespace Nightshift.Tests;

public class ParsingRoutingTests
{
    [Fact]
    public void DotDefaultsAreOrderedScopedAndDoNotRetroactivelyChangeNodes()
    {
        var g = new DotParser().Parse("""
        digraph G {
          graph [goal="a // literal", label="Main"]
          node [timeout=1s, shape=box] edge [weight=3]
          a [prompt="line1\nline2\t\"quoted\"\\"]
          node [timeout=2m]
          subgraph cluster_a { label="Loop A!" node [timeout=3h] b; b -> c [label=next] }
          d; a [label="A"]; a -> b -> d [weight=-1]
          /* ignored */ goal="changed"; // ignored
        }
        """);
        Assert.Equal("1s", g.Nodes["a"].Attributes.Text("timeout"));
        Assert.Equal("3h", g.Nodes["b"].Attributes.Text("timeout"));
        Assert.Equal("2m", g.Nodes["d"].Attributes.Text("timeout"));
        Assert.Contains("loop-a", g.Nodes["b"].Classes);
        Assert.DoesNotContain("loop-a", g.Nodes["d"].Classes);
        Assert.Equal("line1\nline2\t\"quoted\"\\", g.Nodes["a"].Attributes.Text("prompt"));
        Assert.Equal("changed", g.Attributes.Text("goal"));
        Assert.Equal(3, g.Edges.Count); Assert.Equal(3, g.Edges[0].Attributes.Int("weight"));
        Assert.Equal(-1, g.Edges[2].Attributes.Int("weight"));
        Assert.Equal(ValueKind.Duration, g.Nodes["a"].Attributes["timeout"].Kind);
    }
    [Fact]
    public void MultilineStringsAndTypedValuesArePreserved()
    {
        var g = new DotParser().Parse("digraph G { a [prompt=\"first\nsecond\", n=-2, f=.5, b=true, d=250ms, custom.key=bare:value] }");
        Assert.Equal("first\nsecond", g.Nodes["a"].Attributes.Text("prompt"));
        Assert.Equal(ValueKind.Float, g.Nodes["a"].Attributes["f"].Kind);
        Assert.Equal(ValueKind.Boolean, g.Nodes["a"].Attributes["b"].Kind);
        Assert.Equal(25, Durations.Parse("25ms").TotalMilliseconds);
    }
    [Theory]
    [InlineData("graph G { a -- b }")]
    [InlineData("strict digraph G { a -> b }")]
    [InlineData("digraph G { \"a\" }")]
    [InlineData("digraph G { a [label=<html>] }")]
    [InlineData("digraph G { a [label=x shape=box] }")]
    [InlineData("digraph G { a -- b }")]
    [InlineData("digraph G {} digraph H {}")]
    [InlineData("digraph G { a [label=\"bad\\q\"] }")]
    [InlineData("digraph G { /* unterminated")]
    [InlineData("digraph G { a [label=\"unterminated")]
    [InlineData("digraph G { a -> }")]
    public void MalformedOrUnsupportedDotIsRejectedWithLocation(string dot)
    {
        var e = Assert.Throws<DotParseException>(() => new DotParser().Parse(dot)); Assert.True(e.Location.Line > 0); Assert.True(e.Location.Column > 0);
    }
    [Fact]
    public void ValidationProducesStructuralTypedAndCapabilityDiagnostics()
    {
        var p = Preparation.Prepare("digraph G { start [shape=Mdiamond] exit [shape=Msquare] orphan [type=unknown, fidelity=nope, max_retries=-1] start -> exit exit -> start [condition=\"context.x || true\"] }");
        foreach (string rule in new[] { "reachability", "start_no_incoming", "exit_no_outgoing", "type_known", "unsupported_handler", "fidelity_valid", "condition_syntax", "attribute_type" }) Assert.Contains(p.Diagnostics, d => d.Rule == rule);
        Assert.Throws<PipelineException>(() => Preparation.RequireValid(p));
        Assert.Contains(p.Diagnostics, d => d.NodeId == "orphan" && d.Location != null);
    }
    [Fact]
    public void MissingStartExitDanglingEdgesAndDuplicateStartsAreErrors()
    {
        var g = new Graph("G"); g.Nodes["a"] = new("a", new(), new(1, 1)); g.Edges.Add(new("a", "missing", new(), new(1, 2)));
        var d = Validation.Validate(g);
        Assert.Contains(d, x => x.Rule == "start_node"); Assert.Contains(d, x => x.Rule == "terminal_node"); Assert.Contains(d, x => x.Rule == "edge_target_exists");
        var p = Preparation.Prepare("digraph G { start; second [shape=Mdiamond] exit [shape=Msquare] start -> second -> exit }");
        Assert.Contains(p.Diagnostics, x => x.Rule == "start_node");
    }
    private static Edge E(string to, int weight = 0, string condition = "", string label = "")
    {
        var a = new Attributes(); a.Set("weight", weight.ToString()); a.Set("condition", condition); a.Set("label", label); return new("a", to, a, new(1, 1));
    }
    [Fact]
    public void ConditionalEdgesWinThenWeightAndOrdinalLexicalBreakTies()
    {
        var edges = new[] { E("z", 999, label: "yes"), E("b", 5, "outcome=success"), E("A", 5, "outcome=success"), E("a", 5, "outcome=success") };
        Assert.Equal("A", Routing.Select(edges, Outcome.Success() with { PreferredLabel = "yes", SuggestedNextIds = ["z"] }, new())!.To);
        Assert.Equal("b", Routing.Select(new[] { E("a", -1), E("b", 1) }, Outcome.Success(), new())!.To);
    }
    [Theory]
    [InlineData(" [Y] YES ", "yes")]
    [InlineData("Y) Yes", "yes")]
    [InlineData("Y - Yes", "yes")]
    public void PreferredLabelsNormalizeAccelerators(string label, string preferred)
    {
        Assert.Equal("match", Routing.Select(new[] { E("other", 100), E("match", label: label) }, Outcome.Success() with { PreferredLabel = preferred }, new())!.To);
    }
    [Fact]
    public void SuggestionsUseSuggestionOrderAndFalseConditionsNeverFallback()
    {
        var o = Outcome.Success() with { SuggestedNextIds = ["z", "a"] };
        Assert.Equal("z", Routing.Select(new[] { E("a", 9), E("z", -1), E("wrong", 100, "outcome=fail") }, o, new())!.To);
        Assert.Null(Routing.Select(new[] { E("wrong", 100, "outcome=fail", "yes") }, o with { PreferredLabel = "yes" }, new()));
    }
    [Fact]
    public void ConditionsAreCaseSensitiveMissingIsEmptyAndQuotedConjunctionIsLiteral()
    {
        var c = new RunContext(); c.Set("flag", true); c.Set("context.name", "A&&B");
        Assert.True(Conditions.Evaluate("context.missing=\"\" && context.flag=true && context.name=\"A&&B\"", Outcome.Success(), c));
        Assert.False(Conditions.Evaluate("outcome=SUCCESS", Outcome.Success(), c));
        Assert.True(Conditions.Evaluate("preferred_label=Fix && context.flag!=false", Outcome.Success() with { PreferredLabel = "Fix" }, c));
        Assert.True(Conditions.Evaluate("", Outcome.Success(), c));
    }
    [Theory]
    [InlineData("context.x")][InlineData("context.x=true || outcome=fail")][InlineData("context.x>2")][InlineData("outcome==success")][InlineData("outcome=success &&")][InlineData("bad=true")]
    public void ConditionsRejectExtensionsAndMalformedClauses(string condition) => Assert.Throws<FormatException>(() => Conditions.Parse(condition));
    [Fact]
    public void StylesheetCascadeSourcesAndExplicitDefaultsWork()
    {
        var p = Preparation.Prepare("""
        digraph G {
          graph [goal="goal", llm_provider=openai, model_stylesheet="* {llm_model: base;} box {llm_model: shape;} .fast {llm_model: class;} #a {llm_model: id; reasoning_effort: low;} #a {llm_model: later;}"]
          start [shape=Mdiamond] exit [shape=Msquare]
          a [class=fast, prompt="$goal"] b [class=fast, llm_model=explicit] c
          start -> a -> b -> c -> exit
        }
        """);
        Preparation.RequireValid(p); var g = p.Graph;
        Assert.Equal("later", g.Nodes["a"].Attributes.Text("llm_model")); Assert.Equal("explicit", g.Nodes["b"].Attributes.Text("llm_model")); Assert.Equal("shape", g.Nodes["c"].Attributes.Text("llm_model"));
        Assert.Equal("goal", g.Nodes["a"].Attributes.Text("prompt")); Assert.Contains("#a", g.Nodes["a"].ConfigurationSources["llm_model"]);
        Assert.Equal("graph default", g.Nodes["c"].ConfigurationSources["llm_provider"]);
    }
    [Theory]
    [InlineData("* { unknown: x; }")][InlineData("* {llm_model: x")][InlineData("! {llm_model: x;}")][InlineData("* {}")]
    public void StylesheetRejectsInvalidSyntax(string stylesheet) => Assert.Throws<FormatException>(() => Stylesheet.Parse(stylesheet));
    [Fact]
    public void FidelityAndThreadPrecedenceAreIndependent()
    {
        var g = new DotParser().Parse("digraph G { graph [default_fidelity=compact, thread_id=g] a [fidelity=full, thread_id=n] b a -> b [fidelity=truncate, thread_id=e] }");
        var a = g.Nodes["a"]; var edge = g.Edges[0]; var h = Handoff.Resolve(g, a, edge, "p");
        Assert.Equal("truncate", h.Fidelity); Assert.Equal("edge", h.FidelitySource); Assert.Equal("n", h.ThreadKey);
        a.Attributes.Remove("thread_id"); Assert.Equal("e", Handoff.Resolve(g, a, edge, "p").ThreadKey);
        edge.Attributes.Remove("thread_id"); Assert.Equal("g", Handoff.Resolve(g, a, edge, "p").ThreadKey);
        g.Attributes.Remove("thread_id"); a.SubgraphThread = "sub"; Assert.Equal("sub", Handoff.Resolve(g, a, edge, "p").ThreadKey);
        a.SubgraphThread = ""; Assert.Equal("p", Handoff.Resolve(g, a, edge, "p").ThreadKey);
    }
}
