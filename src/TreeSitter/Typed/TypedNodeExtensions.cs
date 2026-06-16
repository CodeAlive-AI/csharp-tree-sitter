namespace TreeSitter.Typed;

/// <summary>
/// Helpers on <see cref="TreeSitter.Node"/> used by generated typed-node accessors
/// to enumerate field children and the unnamed <c>children</c> group. These are
/// additive conveniences over the Layer 1 API; they allocate only the lazy iterator
/// state machine.
/// </summary>
public static class TypedNodeExtensions
{
    /// <summary>
    /// Lazily enumerates every direct child of <paramref name="node"/> whose field
    /// name equals <paramref name="fieldName"/>, in child order. Unlike
    /// <see cref="TreeSitter.Node.ChildByFieldName(string)"/> (which yields only the
    /// first such child), this covers <c>multiple</c> fields. Null children are
    /// skipped.
    /// </summary>
    /// <param name="node">The parent node.</param>
    /// <param name="fieldName">The field name to match.</param>
    public static IEnumerable<Node> ChildrenByFieldName(this Node node, string fieldName)
    {
        uint count = node.ChildCount;
        for (uint i = 0; i < count; i++)
        {
            if (node.FieldNameForChild(i) == fieldName)
            {
                Node child = node.Child(i);
                if (!child.IsNull)
                    yield return child;
            }
        }
    }

    /// <summary>
    /// Lazily enumerates the unnamed <c>children</c> group of <paramref name="node"/>:
    /// its named children that are not attached to any field. Extra nodes (e.g.
    /// comments) are filtered out, matching the semantics of generated <c>children</c>
    /// accessors.
    /// </summary>
    /// <param name="node">The parent node.</param>
    public static IEnumerable<Node> FieldlessChildren(this Node node)
    {
        uint count = node.ChildCount;
        for (uint i = 0; i < count; i++)
        {
            if (node.FieldNameForChild(i) is not null)
                continue;
            Node child = node.Child(i);
            if (child.IsNull || !child.IsNamed || child.IsExtra)
                continue;
            yield return child;
        }
    }
}
