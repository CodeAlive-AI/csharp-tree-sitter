using TreeSitter;
using TreeSitter.Native;

namespace TreeSitter.Tests;

public class SafeHandlesTests
{
    [Fact]
    public void Fresh_handles_are_invalid_until_populated()
    {
        // A newly-constructed owning handle has the null pointer and is therefore
        // invalid (IsInvalid == true).
        Assert.True(new ParserHandle().IsInvalid);
        Assert.True(new TreeHandle().IsInvalid);
        Assert.True(new QueryHandle().IsInvalid);
        Assert.True(new QueryCursorHandle().IsInvalid);
        Assert.True(new LookaheadIteratorHandle().IsInvalid);
    }

    [Fact]
    public void ParserHandle_create_produces_valid_handle_that_releases()
    {
        ParserHandle handle = ParserHandle.Create();
        Assert.False(handle.IsInvalid);
        Assert.False(handle.IsClosed);
        handle.Dispose();
        Assert.True(handle.IsClosed);
        // Double dispose is safe.
        handle.Dispose();
    }

    [Fact]
    public void QueryCursorHandle_create_produces_valid_handle_that_releases()
    {
        QueryCursorHandle handle = QueryCursorHandle.Create();
        Assert.False(handle.IsInvalid);
        handle.Dispose();
        handle.Dispose();
        Assert.True(handle.IsClosed);
    }

    [Fact]
    public void Dispose_through_public_types_is_idempotent()
    {
        // Each disposable owner wraps a TreeSitterHandle; disposing twice must not throw.
        var parser = new Parser(Grammars.Json);
        parser.Dispose();
        parser.Dispose();

        Tree tree = TestData.ParseJson("[1]");
        tree.Dispose();
        tree.Dispose();

        var query = new Query(Grammars.Json, "(number) @n");
        query.Dispose();
        query.Dispose();

        var cursor = new QueryCursor();
        cursor.Dispose();
        cursor.Dispose();
    }
}
