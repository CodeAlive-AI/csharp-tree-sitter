using System.Text;
using TreeSitter;

namespace TreeSitter.Tests;

public class ParserTests
{
    [Fact]
    public void New_parser_has_no_language()
    {
        using var parser = new Parser();
        Assert.Null(parser.Language);
    }

    [Fact]
    public void Ctor_with_language_sets_it()
    {
        using var parser = new Parser(Grammars.Json);
        Assert.Same(Grammars.Json, parser.Language);
    }

    [Fact]
    public void Set_and_clear_language()
    {
        using var parser = new Parser();
        parser.Language = Grammars.Json;
        Assert.Same(Grammars.Json, parser.Language);
        parser.Language = null;
        Assert.Null(parser.Language);
    }

    [Fact]
    public void TrySetLanguage_returns_true_for_compatible()
    {
        using var parser = new Parser();
        Assert.True(parser.TrySetLanguage(Grammars.Json));
        Assert.Same(Grammars.Json, parser.Language);
    }

    [Fact]
    public void TrySetLanguage_null_throws()
    {
        using var parser = new Parser();
        Assert.Throws<ArgumentNullException>(() => parser.TrySetLanguage(null!));
    }

    [Fact]
    public void Parse_without_language_throws_InvalidOperation()
    {
        using var parser = new Parser();
        // Parsing with no language set is a programming error (distinct from a
        // null return, which is reserved for timeout/cancellation).
        Assert.Throws<InvalidOperationException>(() => parser.Parse("{}"));
        Assert.Throws<InvalidOperationException>(() => parser.Parse("{}"u8));
    }

    [Fact]
    public void Parse_string_null_throws()
    {
        using var parser = new Parser(Grammars.Json);
        Assert.Throws<ArgumentNullException>(() => parser.Parse((string)null!));
    }

    [Fact]
    public void Parse_string_produces_tree()
    {
        using var parser = new Parser(Grammars.Json);
        using Tree? tree = parser.Parse("[1, 2, 3]");
        Assert.NotNull(tree);
        Assert.Equal("document", tree!.RootNode.Kind);
    }

    [Fact]
    public void Parse_span_produces_tree()
    {
        using var parser = new Parser(Grammars.Json);
        ReadOnlySpan<byte> src = "{\"k\": true}"u8;
        using Tree? tree = parser.Parse(src);
        Assert.NotNull(tree);
        Assert.False(tree!.RootNode.HasError);
    }

    [Fact]
    public void Parse_empty_source_is_valid()
    {
        using var parser = new Parser(Grammars.Json);
        using Tree? tree = parser.Parse(ReadOnlySpan<byte>.Empty);
        Assert.NotNull(tree);
        Assert.Equal(0u, tree!.RootNode.EndByte);
    }

    [Fact]
    public void Incremental_reparse_with_edited_old_tree()
    {
        using var parser = new Parser(Grammars.Json);
        const string before = "[1, 2]";
        using Tree? oldTree = parser.Parse(before);
        Assert.NotNull(oldTree);

        // Insert a "0" turning the first number 1 -> 10 at byte index 2.
        const string after = "[10, 2]";
        var edit = new InputEdit(
            StartByte: 2, OldEndByte: 2, NewEndByte: 3,
            StartPoint: new Point(0, 2), OldEndPoint: new Point(0, 2), NewEndPoint: new Point(0, 3));
        oldTree!.Edit(edit);

        using Tree? newTree = parser.Parse(after, oldTree);
        Assert.NotNull(newTree);
        Assert.Equal("document", newTree!.RootNode.Kind);
        Assert.False(newTree.RootNode.HasError);

        Range[] changed = newTree.GetChangedRanges(oldTree);
        Assert.NotNull(changed);
    }

    [Fact]
    public void IncludedRanges_round_trip()
    {
        using var parser = new Parser(Grammars.Json);
        var range = new Range(new Point(0, 0), new Point(0, 5), 0, 5);
        parser.IncludedRanges = [range];
        Range[] got = parser.IncludedRanges;
        Assert.Single(got);
        Assert.Equal(0u, got[0].StartByte);
        Assert.Equal(5u, got[0].EndByte);
    }

    [Fact]
    public void IncludedRanges_default_is_whole_document()
    {
        using var parser = new Parser(Grammars.Json);
        // tree-sitter reports the implicit whole-document range as a single max range.
        Range[] def = parser.IncludedRanges;
        Assert.Single(def);
        Assert.Equal(0u, def[0].StartByte);
        Assert.Equal(uint.MaxValue, def[0].EndByte);

        // Setting null normalizes to "whole document" (the same single max range).
        parser.IncludedRanges = null!;
        Range[] afterNull = parser.IncludedRanges;
        Assert.Single(afterNull);
        Assert.Equal(uint.MaxValue, afterNull[0].EndByte);
    }

    [Fact]
    public void IncludedRanges_overlapping_throws()
    {
        using var parser = new Parser(Grammars.Json);
        var a = new Range(new Point(0, 0), new Point(0, 10), 0, 10);
        var b = new Range(new Point(0, 5), new Point(0, 15), 5, 15);
        Assert.Throws<ArgumentException>(() => parser.IncludedRanges = [a, b]);
    }

    [Fact]
    public void IncludedRanges_large_count_uses_heap_path()
    {
        using var parser = new Parser(Grammars.Json);
        // > 16 ranges forces the heap allocation branch in the setter.
        var ranges = new Range[20];
        for (uint i = 0; i < ranges.Length; i++)
            ranges[i] = new Range(new Point(0, i * 2), new Point(0, i * 2 + 1), i * 2, i * 2 + 1);
        parser.IncludedRanges = ranges;
        Assert.Equal(20, parser.IncludedRanges.Length);
    }

    [Fact]
    public void TimeoutMicros_get_set()
    {
        using var parser = new Parser(Grammars.Json);
        Assert.Equal(0ul, parser.TimeoutMicros);
        parser.TimeoutMicros = 1000;
        Assert.Equal(1000ul, parser.TimeoutMicros);
    }

    [Fact]
    public void Tiny_timeout_on_large_input_returns_null()
    {
        using var parser = new Parser(Grammars.Json);
        // Build a large, deeply nested JSON input so parsing takes long enough to
        // trip a 1-microsecond deadline through the progress callback.
        var sb = new StringBuilder();
        int depth = 4000;
        for (int i = 0; i < depth; i++) sb.Append('[');
        for (int i = 0; i < depth; i++) sb.Append(']');
        string big = sb.ToString();

        parser.TimeoutMicros = 1; // 1 microsecond: essentially immediate cancellation.
        Tree? tree = parser.Parse(big);
        // Either cancelled (null) — the expected outcome — or, if the machine is
        // extraordinarily fast, a tree. Assert the cancellation path which is what we
        // exercise here.
        Assert.Null(tree);
    }

    [Fact]
    public void Timeout_with_generous_budget_completes()
    {
        using var parser = new Parser(Grammars.Json);
        parser.TimeoutMicros = 60_000_000; // 60s: completes well within.
        using Tree? tree = parser.Parse("[1, 2, 3, 4, 5]");
        Assert.NotNull(tree);
        Assert.False(tree!.RootNode.HasError);
    }

    [Fact]
    public void Logger_receives_messages_then_can_be_cleared()
    {
        using var parser = new Parser(Grammars.Json);
        var messages = new List<(LogType type, string msg)>();
        parser.Logger = (t, m) => messages.Add((t, m));
        Assert.NotNull(parser.Logger);

        using Tree? tree = parser.Parse("{\"x\": [1, 2]}");
        Assert.NotNull(tree);
        Assert.NotEmpty(messages);
        Assert.Contains(messages, m => m.type is LogType.Parse or LogType.Lex);

        // Clearing the logger releases the self handle and detaches the native logger.
        parser.Logger = null;
        Assert.Null(parser.Logger);
        int countAfterClear = messages.Count;
        using Tree? tree2 = parser.Parse("[3]");
        Assert.Equal(countAfterClear, messages.Count);
    }

    [Fact]
    public void Logger_exception_is_swallowed()
    {
        using var parser = new Parser(Grammars.Json);
        parser.Logger = (_, _) => throw new InvalidOperationException("boom");
        // Must not propagate across the native boundary.
        using Tree? tree = parser.Parse("{\"a\": 1}");
        Assert.NotNull(tree);
    }

    [Fact]
    public void Reset_does_not_throw()
    {
        using var parser = new Parser(Grammars.Json);
        using Tree? tree = parser.Parse("[1]");
        Assert.NotNull(tree);
        parser.Reset();
        using Tree? tree2 = parser.Parse("[2]");
        Assert.NotNull(tree2);
    }

    [Fact]
    public void Double_dispose_is_safe()
    {
        var parser = new Parser(Grammars.Json);
        parser.Dispose();
        parser.Dispose();
    }

    [Fact]
    public void Use_after_dispose_throws()
    {
        var parser = new Parser(Grammars.Json);
        parser.Dispose();
        Assert.Throws<ObjectDisposedException>(() => parser.Reset());
    }

    [Fact]
    public void Dispose_with_logger_attached_is_clean()
    {
        var parser = new Parser(Grammars.Json);
        parser.Logger = (_, _) => { };
        parser.Dispose();
        parser.Dispose();
    }
}
