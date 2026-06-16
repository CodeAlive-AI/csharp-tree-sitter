using System.Text;
using TreeSitter;

namespace TreeSitter.Tests;

public class TreeTests
{
    [Fact]
    public void RootNode_and_language_and_source()
    {
        const string src = "{\"a\": 1}";
        using Tree tree = TestData.ParseJson(src);
        Assert.Equal("document", tree.RootNode.Kind);
        Assert.Same(Grammars.Json, tree.Language);
        Assert.Equal(src, Encoding.UTF8.GetString(tree.Source.Span));
    }

    [Fact]
    public void RootNodeWithOffset_shifts_positions()
    {
        using Tree tree = TestData.ParseJson("[1]");
        Node shifted = tree.RootNodeWithOffset(100, new Point(5, 0));
        Assert.True(shifted.StartByte >= 100);
        Assert.Equal(5u, shifted.StartPoint.Row);
    }

    [Fact]
    public void Copy_produces_independent_tree()
    {
        using Tree tree = TestData.ParseJson("[1, 2]");
        using Tree copy = tree.Copy();
        Assert.Equal(tree.RootNode.Kind, copy.RootNode.Kind);
        Assert.Equal(tree.Source.Length, copy.Source.Length);
        // Disposing the copy does not affect the original.
        copy.Dispose();
        Assert.Equal("document", tree.RootNode.Kind);
    }

    [Fact]
    public void Edit_then_changed_ranges()
    {
        using var parser = new Parser(Grammars.Json);
        using Tree? oldTree = parser.Parse("[1, 2]");
        Assert.NotNull(oldTree);

        var edit = new InputEdit(1, 1, 4, new Point(0, 1), new Point(0, 1), new Point(0, 4));
        oldTree!.Edit(edit);
        using Tree? newTree = parser.Parse("[999, 1, 2]", oldTree);
        Assert.NotNull(newTree);

        Range[] changed = newTree!.GetChangedRanges(oldTree);
        Assert.NotNull(changed);
        // Ranges should be well-formed.
        foreach (Range r in changed)
            Assert.True(r.EndByte >= r.StartByte);
    }

    [Fact]
    public void GetChangedRanges_null_throws()
    {
        using Tree tree = TestData.ParseJson("[1]");
        Assert.Throws<ArgumentNullException>(() => tree.GetChangedRanges(null!));
    }

    [Fact]
    public void GetChangedRanges_identical_trees_is_empty()
    {
        using var parser = new Parser(Grammars.Json);
        using Tree? a = parser.Parse("[1, 2]");
        using Tree? b = parser.Parse("[1, 2]");
        Assert.NotNull(a);
        Assert.NotNull(b);
        Range[] changed = b!.GetChangedRanges(a!);
        Assert.Empty(changed);
    }

    [Fact]
    public void IncludedRanges_reflects_whole_document()
    {
        using Tree tree = TestData.ParseJson("[1]");
        Range[] ranges = tree.IncludedRanges;
        Assert.NotNull(ranges);
        Assert.Single(ranges); // whole-document is a single (max) range.
    }

    [Fact]
    public void IncludedRanges_round_trips_explicit_ranges()
    {
        const string src = "[1, 2, 3, 4]";
        using var parser = new Parser(Grammars.Json);
        var included = new Range(new Point(0, 0), new Point(0, 6), 0, 6);
        parser.IncludedRanges = [included];
        using Tree? tree = parser.Parse(src);
        Assert.NotNull(tree);
        Range[] ranges = tree!.IncludedRanges;
        Assert.Single(ranges);
        Assert.Equal(0u, ranges[0].StartByte);
        Assert.Equal(6u, ranges[0].EndByte);
    }

    [Fact]
    public void Double_dispose_is_safe()
    {
        Tree tree = TestData.ParseJson("[1]");
        tree.Dispose();
        tree.Dispose();
    }

    [Fact]
    public void Use_after_dispose_throws()
    {
        Tree tree = TestData.ParseJson("[1]");
        tree.Dispose();
        Assert.Throws<ObjectDisposedException>(() => tree.RootNode);
    }

    [Fact]
    public void Nodes_remain_usable_while_their_tree_is_alive()
    {
        // Pins the SAFE side of the Node lifetime contract: extracted nodes (and their
        // descendants) stay fully usable for the entire lifetime of the undisposed tree.
        // (Using a node AFTER its tree is disposed is undefined and intentionally not
        // exercised here.)
        using Tree tree = TestData.ParseJson("[1, \"two\", 3]");
        Node root = tree.RootNode;
        Node array = root.NamedChild(0);
        Node first = array.NamedChild(0);
        Node second = array.NamedChild(1);

        // Repeated native reads via the retained node values keep returning correct
        // results as long as the tree is alive.
        Assert.Equal("document", root.Kind);
        Assert.Equal("array", array.Kind);
        Assert.Equal("number", first.Kind);
        Assert.Equal("1", first.Text);
        Assert.Equal("string", second.Kind);
        Assert.Equal("\"two\"", second.Text);
        Assert.Equal(3u, array.NamedChildCount);
        // Navigating again from a previously-extracted node still works.
        Assert.Equal("number", array.NamedChild(2).Kind);
    }
}
