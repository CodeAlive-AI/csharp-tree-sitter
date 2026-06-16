using TreeSitter;

namespace TreeSitter.Tests;

public class TreeCursorTests
{
    private static Tree Tree(string src) => TestData.ParseJson(src);

    [Fact]
    public void Current_reflects_root_then_navigation()
    {
        using Tree tree = Tree("{\"k\": [1, 2]}");
        using TreeCursor cursor = tree.RootNode.Walk();
        Assert.Equal("document", cursor.Current.Kind);
        Assert.Equal(0u, cursor.CurrentDepth);
        Assert.Equal(0u, cursor.CurrentDescendantIndex);

        Assert.True(cursor.GotoFirstChild());
        Assert.Equal("object", cursor.Current.Kind);
        Assert.Equal(1u, cursor.CurrentDepth);
    }

    [Fact]
    public void Field_name_and_id()
    {
        using Tree tree = Tree("{\"k\": 1}");
        using TreeCursor cursor = tree.RootNode.Walk();
        cursor.GotoFirstChild();   // object
        cursor.GotoFirstChild();   // {
        // walk to the pair, then into its key field
        while (cursor.Current.Kind != "pair" && cursor.GotoNextSibling()) { }
        Assert.Equal("pair", cursor.Current.Kind);
        Assert.True(cursor.GotoFirstChild()); // key (string)
        Assert.Equal("key", cursor.CurrentFieldName);
        Assert.True(cursor.CurrentFieldId > 0);
    }

    [Fact]
    public void CurrentFieldName_null_when_no_field()
    {
        using Tree tree = Tree("[1, 2]");
        using TreeCursor cursor = tree.RootNode.Walk();
        cursor.GotoFirstChild(); // array
        cursor.GotoFirstChild(); // "[" — no field
        Assert.Null(cursor.CurrentFieldName);
        Assert.Equal(0, cursor.CurrentFieldId);
    }

    [Fact]
    public void Goto_first_last_child_and_siblings()
    {
        using Tree tree = Tree("[1, 2, 3]");
        using TreeCursor cursor = tree.RootNode.Walk();
        cursor.GotoFirstChild(); // array
        Assert.True(cursor.GotoLastChild()); // "]"
        Assert.Equal("]", cursor.Current.Kind);
        Assert.True(cursor.GotoPreviousSibling());
        Assert.True(cursor.GotoNextSibling());
        Assert.Equal("]", cursor.Current.Kind);
    }

    [Fact]
    public void Goto_parent()
    {
        using Tree tree = Tree("[1]");
        using TreeCursor cursor = tree.RootNode.Walk();
        cursor.GotoFirstChild();
        cursor.GotoFirstChild();
        Assert.True(cursor.GotoParent());
        Assert.Equal("array", cursor.Current.Kind);
    }

    [Fact]
    public void Goto_first_child_for_byte_and_point()
    {
        using Tree tree = Tree("[1, 22, 333]");
        using TreeCursor cursor = tree.RootNode.Walk();
        cursor.GotoFirstChild(); // array
        long idx = cursor.GotoFirstChildForByte(tree.RootNode.NamedChild(0).NamedChild(1).StartByte);
        Assert.True(idx >= 0);

        using TreeCursor cursor2 = tree.RootNode.Walk();
        cursor2.GotoFirstChild();
        long idx2 = cursor2.GotoFirstChildForPoint(new Point(0, 4));
        Assert.True(idx2 >= 0);
    }

    [Fact]
    public void Goto_first_child_for_byte_out_of_range_returns_negative()
    {
        using Tree tree = Tree("[1]");
        using TreeCursor cursor = tree.RootNode.Walk();
        long idx = cursor.GotoFirstChildForByte(99999);
        Assert.Equal(-1, idx);
    }

    [Fact]
    public void Goto_descendant_and_reset()
    {
        using Tree tree = Tree("[1, 2, 3]");
        using TreeCursor cursor = tree.RootNode.Walk();
        cursor.GotoDescendant(2);
        Assert.Equal(2u, cursor.CurrentDescendantIndex);

        cursor.Reset(tree.RootNode);
        Assert.Equal("document", cursor.Current.Kind);
        Assert.Equal(0u, cursor.CurrentDescendantIndex);
    }

    [Fact]
    public void ResetTo_copies_position()
    {
        using Tree tree = Tree("[1, 2, 3]");
        using TreeCursor a = tree.RootNode.Walk();
        a.GotoDescendant(3);
        using TreeCursor b = tree.RootNode.Walk();
        b.ResetTo(a);
        Assert.Equal(a.CurrentDescendantIndex, b.CurrentDescendantIndex);
    }

    [Fact]
    public void ResetTo_null_throws()
    {
        using Tree tree = Tree("[1]");
        using TreeCursor a = tree.RootNode.Walk();
        Assert.Throws<ArgumentNullException>(() => a.ResetTo(null!));
    }

    [Fact]
    public void Copy_is_independent()
    {
        using Tree tree = Tree("[1, 2, 3]");
        using TreeCursor a = tree.RootNode.Walk();
        a.GotoFirstChild();
        using TreeCursor b = a.Copy();
        Assert.Equal(a.Current.Kind, b.Current.Kind);
        b.GotoFirstChild();
        // Advancing the copy does not move the original.
        Assert.Equal("array", a.Current.Kind);
    }

    [Fact]
    public void Double_dispose_is_safe()
    {
        using Tree tree = Tree("[1]");
        var cursor = tree.RootNode.Walk();
        cursor.Dispose();
        cursor.Dispose();
    }

    [Fact]
    public void Use_after_dispose_throws()
    {
        using Tree tree = Tree("[1]");
        var cursor = tree.RootNode.Walk();
        cursor.Dispose();
        Assert.Throws<ObjectDisposedException>(() => cursor.Current);
        Assert.Throws<ObjectDisposedException>(() => cursor.GotoFirstChild());
        Assert.Throws<ObjectDisposedException>(() => cursor.CurrentFieldName);
        Assert.Throws<ObjectDisposedException>(() => cursor.CurrentFieldId);
        Assert.Throws<ObjectDisposedException>(() => cursor.CurrentDepth);
        Assert.Throws<ObjectDisposedException>(() => cursor.CurrentDescendantIndex);
        Assert.Throws<ObjectDisposedException>(() => cursor.GotoParent());
        Assert.Throws<ObjectDisposedException>(() => cursor.GotoLastChild());
        Assert.Throws<ObjectDisposedException>(() => cursor.GotoNextSibling());
        Assert.Throws<ObjectDisposedException>(() => cursor.GotoPreviousSibling());
        Assert.Throws<ObjectDisposedException>(() => cursor.GotoFirstChildForByte(0));
        Assert.Throws<ObjectDisposedException>(() => cursor.GotoFirstChildForPoint(Point.Zero));
        Assert.Throws<ObjectDisposedException>(() => cursor.GotoDescendant(0));
        Assert.Throws<ObjectDisposedException>(() => cursor.Reset(tree.RootNode));
        Assert.Throws<ObjectDisposedException>(() => cursor.Copy());
    }
}
