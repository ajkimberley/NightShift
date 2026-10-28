using System.Text;
using System.Text.RegularExpressions;

namespace Nightshift.Core;

public sealed class DotParseException(string message, SourceLocation location) : Exception($"{location.Line}:{location.Column}: {message}")
{
    public SourceLocation Location { get; } = location;
}

// A tokenizing, recursive-descent parser. String/comment boundaries are handled lexically.
internal sealed record Token(string Text, bool Quoted, SourceLocation Location);
internal sealed class Lexer(string source)
{
    private int index, line = 1, column = 1;
    private char Peek(int offset = 0) => index + offset < source.Length ? source[index + offset] : '\0';
    private char Take() { char c = source[index++]; if (c == '\n') { line++; column = 1; } else column++; return c; }
    public List<Token> Scan()
    {
        List<Token> tokens = [];
        while (index < source.Length)
        {
            if (char.IsWhiteSpace(Peek())) { Take(); continue; }
            if (Peek() == '/' && Peek(1) == '/') { while (index < source.Length && Take() != '\n') { } continue; }
            var loc = new SourceLocation(line, column);
            if (Peek() == '/' && Peek(1) == '*')
            {
                Take(); Take();
                while (index < source.Length && !(Peek() == '*' && Peek(1) == '/')) Take();
                if (index == source.Length) throw new DotParseException("Unterminated comment", loc);
                Take(); Take(); continue;
            }
            if (Peek() == '"')
            {
                Take(); var b = new StringBuilder();
                while (index < source.Length && Peek() != '"')
                {
                    char c = Take();
                    if (c == '\\')
                    {
                        if (index == source.Length) throw new DotParseException("Unterminated escape", loc);
                        c = Take() switch { 'n' => '\n', 't' => '\t', '"' => '"', '\\' => '\\', _ => throw new DotParseException("Unsupported escape", loc) };
                    }
                    b.Append(c);
                }
                if (index == source.Length) throw new DotParseException("Unterminated string", loc);
                Take(); tokens.Add(new(b.ToString(), true, loc)); continue;
            }
            if (Peek() == '-' && Peek(1) == '>') { Take(); Take(); tokens.Add(new("->", false, loc)); continue; }
            if ("{}[]=,;".Contains(Peek())) { tokens.Add(new(Take().ToString(), false, loc)); continue; }
            var word = new StringBuilder();
            while (index < source.Length && !char.IsWhiteSpace(Peek()) && !"{}[]=,;\"".Contains(Peek()) && !(Peek() == '-' && Peek(1) == '>')) word.Append(Take());
            if (word.Length == 0) throw new DotParseException("Unexpected character", loc);
            tokens.Add(new(word.ToString(), false, loc));
        }
        tokens.Add(new("<eof>", false, new(line, column))); return tokens;
    }
}

public sealed class DotParser
{
    private List<Token> tokens = []; private int position; private Graph graph = null!;
    private Token Current => tokens[position];
    private bool Is(string text) => !Current.Quoted && Current.Text == text;
    private bool Accept(string text) { if (!Is(text)) return false; position++; return true; }
    private Token Take() => tokens[position++];
    private Token Expect(string text) => Accept(text) ? tokens[position - 1] : throw Error($"Expected '{text}', got '{Current.Text}'");
    private DotParseException Error(string text) => new(text, Current.Location);
    private string Identifier()
    {
        if (Current.Quoted || !Regex.IsMatch(Current.Text, @"^[A-Za-z_][A-Za-z0-9_]*$")) throw Error("Expected bare identifier");
        return Take().Text;
    }
    public Graph Parse(string source)
    {
        tokens = new Lexer(source).Scan(); position = 0;
        Expect("digraph"); graph = new(Identifier()); Expect("{");
        Scope(new(), new(), graph.Attributes, []);
        Expect("<eof>"); return graph;
    }
    private void Scope(Attributes nodeDefaults, Attributes edgeDefaults, Attributes graphDefaults, List<Node> members)
    {
        var localMembers = new List<Node>();
        while (!Accept("}"))
        {
            if (Is("<eof>")) throw Error("Unclosed graph/subgraph");
            if (Accept(";")) continue;
            if (Accept("subgraph"))
            {
                if (!Is("{")) Identifier(); Expect("{");
                Scope(new(nodeDefaults), new(edgeDefaults), new(graphDefaults), localMembers); Accept(";"); continue;
            }
            var location = Current.Location; string id = Identifier();
            if (id is "graph" or "node" or "edge" && Is("["))
            {
                var a = Blocks(); (id == "graph" ? graphDefaults : id == "node" ? nodeDefaults : edgeDefaults).Merge(a);
            }
            else if (Accept("=")) graphDefaults[id] = Value();
            else
            {
                Node first = EnsureNode(id, nodeDefaults, location); localMembers.Add(first);
                if (Accept("->"))
                {
                    var chain = new List<string> { id };
                    do { var l = Current.Location; string next = Identifier(); chain.Add(next); localMembers.Add(EnsureNode(next, nodeDefaults, l)); } while (Accept("->"));
                    var attributes = new Attributes(edgeDefaults); attributes.Merge(Blocks());
                    for (int i = 1; i < chain.Count; i++) graph.Edges.Add(new(chain[i - 1], chain[i], new(attributes), location));
                }
                else
                {
                    var a = Blocks(); first.Attributes.Merge(a); first.ExplicitKeys.UnionWith(a.Keys);
                }
            }
            Accept(";");
        }
        if (!ReferenceEquals(graphDefaults, graph.Attributes))
        {
            string label = graphDefaults.Text("label");
            string cls = Regex.Replace(label.ToLowerInvariant().Replace(' ', '-'), "[^a-z0-9-]", "");
            if (cls.Length > 0) foreach (var node in localMembers) { node.Classes.Add(cls); node.SubgraphThread = node.SubgraphThread.Length == 0 ? cls : node.SubgraphThread; }
        }
        members.AddRange(localMembers);
    }
    private Node EnsureNode(string id, Attributes defaults, SourceLocation location)
    {
        if (!graph.Nodes.TryGetValue(id, out var node)) graph.Nodes[id] = node = new(id, new(defaults), location);
        return node;
    }
    private Attributes Blocks()
    {
        var result = new Attributes();
        while (Accept("["))
        {
            if (Accept("]")) continue;
            do
            {
                if (Current.Quoted || !Regex.IsMatch(Current.Text, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")) throw Error("Invalid attribute key");
                string key = Take().Text; Expect("="); result[key] = Value();
            } while (Accept(","));
            Expect("]");
        }
        return result;
    }
    private AttributeValue Value()
    {
        var t = Take(); ValueKind kind;
        if (t.Quoted) kind = ValueKind.String;
        else if (Regex.IsMatch(t.Text, @"^-?\d+$")) kind = ValueKind.Integer;
        else if (Regex.IsMatch(t.Text, @"^-?\d*\.\d+$")) kind = ValueKind.Float;
        else if (t.Text is "true" or "false") kind = ValueKind.Boolean;
        else if (Regex.IsMatch(t.Text, @"^-?\d+(ms|s|m|h|d)$")) kind = ValueKind.Duration;
        else if (Regex.IsMatch(t.Text, @"^[A-Za-z_][A-Za-z0-9_.:-]*$")) kind = ValueKind.Bare;
        else throw new DotParseException($"Unsupported attribute value '{t.Text}'", t.Location);
        return new(t.Text, kind, t.Location);
    }
}
