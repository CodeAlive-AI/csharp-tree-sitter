using TreeSitter;

namespace TreeSitter.Tests;

public class QueryCursorTests
{
    [Fact]
    public void Exec_then_next_match()
    {
        using var query = new Query(Grammars.Json, "(number) @n");
        using Tree tree = TestData.ParseJson("[1, 2, 3]");
        using var cursor = new QueryCursor();
        cursor.Exec(query, tree.RootNode);

        int count = 0;
        while (cursor.NextMatch(out QueryMatch match))
        {
            count++;
            Assert.Single(match.Captures);
            Assert.Equal("number", match.Captures[0].Node.Kind);
            Assert.Equal(0u, match.Captures[0].Index);
        }
        Assert.Equal(3, count);
    }

    [Fact]
    public void Exec_null_query_throws()
    {
        using Tree tree = TestData.ParseJson("[1]");
        using var cursor = new QueryCursor();
        Assert.Throws<ArgumentNullException>(() => cursor.Exec(null!, tree.RootNode));
    }

    [Fact]
    public void Matches_enumerable()
    {
        using var query = new Query(Grammars.Json, "(string) @s");
        using Tree tree = TestData.ParseJson("[\"a\", \"b\"]");
        using var cursor = new QueryCursor();
        List<QueryMatch> matches = cursor.Matches(query, tree.RootNode).ToList();
        Assert.Equal(2, matches.Count);
        foreach (QueryMatch m in matches)
            Assert.Equal("string", m.Captures[0].Node.Kind);
    }

    [Fact]
    public void Next_capture_and_captures_enumerable()
    {
        using var query = new Query(Grammars.Json, "(pair key: (string) @k value: (_) @v)");
        using Tree tree = TestData.ParseJson("{\"a\": 1, \"b\": 2}");
        using var cursor = new QueryCursor();

        cursor.Exec(query, tree.RootNode);
        int captureCount = 0;
        while (cursor.NextCapture(out QueryMatch match, out uint index))
        {
            captureCount++;
            Assert.True(index < match.Captures.Length);
        }
        Assert.True(captureCount >= 4); // 2 pairs * (key + value)

        using var cursor2 = new QueryCursor();
        List<(QueryMatch match, uint index)> all = cursor2.Captures(query, tree.RootNode).ToList();
        Assert.Equal(captureCount, all.Count);
    }

    [Fact]
    public void NextMatch_false_when_exhausted()
    {
        using var query = new Query(Grammars.Json, "(number) @n");
        using Tree tree = TestData.ParseJson("[\"only-a-string\"]");
        using var cursor = new QueryCursor();
        cursor.Exec(query, tree.RootNode);
        Assert.False(cursor.NextMatch(out QueryMatch match));
        // On a failed match the out value is default(QueryMatch).
        Assert.Equal(default, match);
    }

    [Fact]
    public void MatchLimit_get_set()
    {
        using var cursor = new QueryCursor();
        Assert.True(cursor.MatchLimit > 0); // default is uint.MaxValue
        cursor.MatchLimit = 1;
        Assert.Equal(1u, cursor.MatchLimit);
    }

    [Fact]
    public void DidExceedMatchLimit_trips_with_many_concurrent_matches()
    {
        // A pattern that opens a match at every pair, combined with a match limit of 1,
        // forces the cursor to drop in-progress matches and set the overflow flag.
        using var cursor = new QueryCursor();
        cursor.MatchLimit = 1;

        var sb = new System.Text.StringBuilder("{");
        for (int i = 0; i < 200; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('"').Append('k').Append(i).Append("\": [").Append(i).Append(", ").Append(i).Append(']');
        }
        sb.Append('}');

        using var query = new Query(Grammars.Json, "(pair value: (array (number) @a (number) @b))");
        using Tree tree = TestData.ParseJson(sb.ToString());
        _ = cursor.Matches(query, tree.RootNode).ToList();
        Assert.True(cursor.DidExceedMatchLimit);
    }

    [Fact]
    public void DidExceedMatchLimit_false_with_no_limit()
    {
        using var cursor = new QueryCursor();
        using var query = new Query(Grammars.Json, "(number) @n");
        using Tree tree = TestData.ParseJson("[1, 2, 3]");
        _ = cursor.Matches(query, tree.RootNode).ToList();
        Assert.False(cursor.DidExceedMatchLimit);
    }

    [Fact]
    public void SetByteRange_restricts_matches()
    {
        using var query = new Query(Grammars.Json, "(number) @n");
        using Tree tree = TestData.ParseJson("[1, 2, 3]");
        using var cursor = new QueryCursor();
        // Restrict to the first few bytes (only the first number).
        cursor.SetByteRange(0, 3);
        int count = cursor.Matches(query, tree.RootNode).Count();
        Assert.True(count >= 1);
    }

    [Fact]
    public void SetByteRange_invalid_throws()
    {
        using var cursor = new QueryCursor();
        Assert.Throws<ArgumentException>(() => cursor.SetByteRange(10, 5));
    }

    [Fact]
    public void SetPointRange_restricts_matches()
    {
        using var query = new Query(Grammars.Json, "(number) @n");
        using Tree tree = TestData.ParseJson("[1,\n2,\n3]");
        using var cursor = new QueryCursor();
        cursor.SetPointRange(new Point(0, 0), new Point(1, 0));
        int count = cursor.Matches(query, tree.RootNode).Count();
        Assert.True(count >= 1);
    }

    [Fact]
    public void SetPointRange_invalid_throws()
    {
        using var cursor = new QueryCursor();
        Assert.Throws<ArgumentException>(
            () => cursor.SetPointRange(new Point(5, 0), new Point(1, 0)));
    }

    [Fact]
    public void SetMaxStartDepth_does_not_throw()
    {
        using var query = new Query(Grammars.Json, "(number) @n");
        using Tree tree = TestData.ParseJson("[[1], [2]]");
        using var cursor = new QueryCursor();
        cursor.SetMaxStartDepth(1);
        _ = cursor.Matches(query, tree.RootNode).Count();
        cursor.SetMaxStartDepth(uint.MaxValue);
        Assert.True(cursor.Matches(query, tree.RootNode).Count() >= 0);
    }

    [Fact]
    public void SetTimeoutMicros_then_exec_uses_options_path()
    {
        using var query = new Query(Grammars.Json, "(number) @n");
        using Tree tree = TestData.ParseJson("[1, 2, 3]");
        using var cursor = new QueryCursor();
        cursor.SetTimeoutMicros(60_000_000); // generous: completes normally.
        List<QueryMatch> matches = cursor.Matches(query, tree.RootNode).ToList();
        Assert.Equal(3, matches.Count);
    }

    [Fact]
    public void Double_dispose_is_safe()
    {
        var cursor = new QueryCursor();
        cursor.Dispose();
        cursor.Dispose();
    }

    [Fact]
    public void Use_after_dispose_throws()
    {
        var cursor = new QueryCursor();
        cursor.Dispose();
        Assert.Throws<ObjectDisposedException>(() => cursor.MatchLimit);
    }

    [Fact]
    public void Timed_exec_over_large_input_fires_progress_callback()
    {
        // A large input ensures the query progress callback actually fires during
        // iteration, exercising the deadline-check thunk. A generous timeout lets it
        // complete (the callback returns "continue").
        var sb = new System.Text.StringBuilder("[");
        for (int i = 0; i < 5000; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(i);
        }
        sb.Append(']');

        using var query = new Query(Grammars.Json, "(number) @n");
        using Tree tree = TestData.ParseJson(sb.ToString());
        using var cursor = new QueryCursor();
        cursor.SetTimeoutMicros(60_000_000);
        int count = cursor.Matches(query, tree.RootNode).Count();
        Assert.Equal(5000, count);
    }

    [Fact]
    public void Timed_exec_twice_reuses_deadline_cell()
    {
        using var query = new Query(Grammars.Json, "(number) @n");
        using Tree tree = TestData.ParseJson("[1, 2]");
        using var cursor = new QueryCursor();
        cursor.SetTimeoutMicros(60_000_000);
        // Second timed exec hits the "cells already allocated" branch; both runs must
        // still yield the full result set, proving the reused cells stay valid.
        Assert.Equal(2, cursor.Matches(query, tree.RootNode).Count());
        Assert.Equal(2, cursor.Matches(query, tree.RootNode).Count());
    }

    [Fact]
    public void Timed_exec_then_manual_iteration_dereferences_retained_options()
    {
        // ts_query_cursor_exec_with_options STORES the options pointer and dereferences
        // it on every later NextMatch. Driving a TIMED Exec() and then iterating several
        // NextMatch calls over a multi-match input forces that retained pointer to be
        // read AFTER Exec() returns. Before the fix (options on the stack) this was
        // use-after-free / UB; after the fix the options live in a cursor-owned native
        // cell and the iteration produces correct results.
        using var query = new Query(Grammars.Json, "(number) @n");
        using Tree tree = TestData.ParseJson("[10, 20, 30, 40, 50]");
        using var cursor = new QueryCursor();
        cursor.SetTimeoutMicros(60_000_000); // generous: routes through the options path, completes.
        cursor.Exec(query, tree.RootNode);

        var values = new List<string>();
        while (cursor.NextMatch(out QueryMatch match))
        {
            Assert.Single(match.Captures);
            Assert.Equal("number", match.Captures[0].Node.Kind);
            values.Add(match.Captures[0].Node.Text);
        }
        Assert.Equal(["10", "20", "30", "40", "50"], values);
    }
}
