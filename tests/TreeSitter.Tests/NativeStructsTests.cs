using TreeSitter.Native;

namespace TreeSitter.Tests;

/// <summary>
/// Exercises the blittable interop structs' managed-side helpers (equality on
/// <see cref="TSNode"/>, the <see cref="TSNode.IsZero"/> sentinel). The structs are
/// internal; these tests reach them via <c>InternalsVisibleTo</c>.
/// </summary>
public class NativeStructsTests
{
    [Fact]
    public void TSNode_IsZero_and_equality()
    {
        var zero = default(TSNode);
        Assert.True(zero.IsZero);

        var a = new TSNode { Context0 = 1, Context1 = 2, Context2 = 3, Context3 = 4, Id = 10, Tree = 20 };
        var b = new TSNode { Context0 = 1, Context1 = 2, Context2 = 3, Context3 = 4, Id = 10, Tree = 20 };
        var c = new TSNode { Context0 = 9, Context1 = 2, Context2 = 3, Context3 = 4, Id = 10, Tree = 20 };

        Assert.False(a.IsZero);
        Assert.True(a.Equals(b));
        Assert.True(a.Equals((object)b));
        Assert.False(a.Equals(c));
        Assert.False(a.Equals((object?)null));
        Assert.False(a.Equals("not a node"));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void TSNode_equality_distinguishes_each_field()
    {
        var baseNode = new TSNode { Context0 = 1, Context1 = 1, Context2 = 1, Context3 = 1, Id = 1, Tree = 1 };
        Assert.False(baseNode.Equals(baseNode with { Context1 = 2 }));
        Assert.False(baseNode.Equals(baseNode with { Context2 = 2 }));
        Assert.False(baseNode.Equals(baseNode with { Context3 = 2 }));
        Assert.False(baseNode.Equals(baseNode with { Id = 2 }));
        Assert.False(baseNode.Equals(baseNode with { Tree = 2 }));
    }
}
