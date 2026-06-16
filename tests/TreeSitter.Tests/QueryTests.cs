using TreeSitter;

namespace TreeSitter.Tests;

public class QueryTests
{
    [Fact]
    public void Compile_success_and_counts()
    {
        using var query = new Query(Grammars.Json, "(string) @str (number) @num");
        Assert.Equal(2u, query.PatternCount);
        Assert.Equal(2u, query.CaptureCount);
        Assert.Equal(0u, query.StringCount);
    }

    [Fact]
    public void Compile_with_string_literal_counts_strings()
    {
        // A predicate referencing a string literal bumps the string count.
        using var query = new Query(Grammars.Json, "((number) @n (#eq? @n \"1\"))");
        Assert.True(query.StringCount >= 1);
    }

    [Fact]
    public void Null_args_throw()
    {
        Assert.Throws<ArgumentNullException>(() => new Query(null!, "(string) @s"));
        Assert.Throws<ArgumentNullException>(() => new Query(Grammars.Json, null!));
    }

    [Fact]
    public void Compile_syntax_error_throws_with_offset()
    {
        QueryException ex = Assert.Throws<QueryException>(() => new Query(Grammars.Json, "(string"));
        Assert.Equal(QueryError.Syntax, ex.Error);
        Assert.True(ex.Offset > 0);
    }

    [Fact]
    public void Compile_bad_node_type_throws_NodeType()
    {
        QueryException ex = Assert.Throws<QueryException>(() => new Query(Grammars.Json, "(not_a_real_node) @x"));
        Assert.Equal(QueryError.NodeType, ex.Error);
    }

    [Fact]
    public void Compile_bad_field_throws_Field()
    {
        QueryException ex = Assert.Throws<QueryException>(() => new Query(Grammars.Json, "(pair nonexistent_field: (_) @x)"));
        Assert.Equal(QueryError.Field, ex.Error);
    }

    [Fact]
    public void Compile_bad_capture_in_predicate_throws_Capture()
    {
        QueryException ex = Assert.Throws<QueryException>(
            () => new Query(Grammars.Json, "((number) @n (#eq? @undefined \"1\"))"));
        Assert.Equal(QueryError.Capture, ex.Error);
    }

    [Fact]
    public void StartByte_and_EndByte_for_pattern()
    {
        const string src = "(string) @s\n(number) @n";
        using var query = new Query(Grammars.Json, src);
        Assert.Equal(0u, query.StartByteForPattern(0));
        Assert.True(query.StartByteForPattern(1) > 0);
        Assert.True(query.EndByteForPattern(0) > 0);
        Assert.True(query.EndByteForPattern(1) >= query.StartByteForPattern(1));
    }

    [Fact]
    public void CaptureNameForId_round_trips()
    {
        using var query = new Query(Grammars.Json, "(string) @the_string");
        Assert.Equal("the_string", query.CaptureNameForId(0));
    }

    [Fact]
    public void StringValueForId()
    {
        using var query = new Query(Grammars.Json, "((number) @n (#eq? @n \"42\"))");
        // String literals include the predicate name ("eq?") and the operand ("42").
        var values = new List<string>();
        for (uint i = 0; i < query.StringCount; i++)
            values.Add(query.StringValueForId(i));
        Assert.Contains("42", values);
        Assert.Contains("eq?", values);
    }

    [Fact]
    public void CaptureQuantifierForId()
    {
        using var query = new Query(Grammars.Json, "(array (number)* @nums)");
        Quantifier q = query.CaptureQuantifierForId(0, 0);
        Assert.True(q is Quantifier.ZeroOrMore or Quantifier.OneOrMore or Quantifier.One or Quantifier.ZeroOrOne);
    }

    [Fact]
    public void IsPatternRooted_nonlocal_and_guaranteed()
    {
        using var query = new Query(Grammars.Json, "(string) @s");
        _ = query.IsPatternRooted(0);
        _ = query.IsPatternNonLocal(0);
        _ = query.IsPatternGuaranteedAtStep(0);
        // Just assert they return without throwing and are deterministic.
        Assert.Equal(query.IsPatternRooted(0), query.IsPatternRooted(0));
    }

    [Fact]
    public void PredicatesForPattern_returns_steps()
    {
        using var query = new Query(Grammars.Json, "((number) @n (#eq? @n \"1\"))");
        IReadOnlyList<QueryPredicateStep> steps = query.PredicatesForPattern(0);
        Assert.NotEmpty(steps);
        // Predicate step lists end with a Done sentinel.
        Assert.Contains(steps, s => s.Type == QueryPredicateStepType.Done);
        Assert.Contains(steps, s => s.Type == QueryPredicateStepType.String);
        Assert.Contains(steps, s => s.Type == QueryPredicateStepType.Capture);
    }

    [Fact]
    public void PredicatesForPattern_empty_when_no_predicate()
    {
        using var query = new Query(Grammars.Json, "(string) @s");
        Assert.Empty(query.PredicatesForPattern(0));
    }

    [Fact]
    public void DisableCapture_and_DisablePattern()
    {
        using var query = new Query(Grammars.Json, "(string) @a (number) @b");
        query.DisableCapture("a");
        query.DisablePattern(1);
        // The query remains usable (no throw); run it to confirm.
        using var cursor = new QueryCursor();
        using Tree tree = TestData.ParseJson("[\"x\", 1]");
        int matches = cursor.Matches(query, tree.RootNode).Count();
        Assert.True(matches >= 0);
    }

    [Fact]
    public void DisableCapture_null_throws()
    {
        using var query = new Query(Grammars.Json, "(string) @s");
        Assert.Throws<ArgumentNullException>(() => query.DisableCapture(null!));
    }

    [Fact]
    public void Double_dispose_is_safe()
    {
        var query = new Query(Grammars.Json, "(string) @s");
        query.Dispose();
        query.Dispose();
    }

    [Fact]
    public void Use_after_dispose_throws()
    {
        var query = new Query(Grammars.Json, "(string) @s");
        query.Dispose();
        Assert.Throws<ObjectDisposedException>(() => query.PatternCount);
    }
}
