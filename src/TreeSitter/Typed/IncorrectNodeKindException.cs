namespace TreeSitter.Typed;

/// <summary>
/// Thrown when an untyped <see cref="TreeSitter.Node"/> is forced into a typed
/// wrapper whose <see cref="ITypedNode{TSelf}.Accepts(string)"/> rejects the node's
/// kind — for example a required typed field whose child is missing or has an
/// unexpected kind, or a failed <see cref="UntypedNode.Cast{T}"/>.
/// </summary>
public sealed class IncorrectNodeKindException : TreeSitterException
{
    /// <summary>The node whose kind did not match what was expected.</summary>
    public Node Node { get; }

    /// <summary>The name of the typed wrapper that rejected the node.</summary>
    public string ExpectedType { get; }

    /// <summary>
    /// A human-readable description of the kinds the wrapper would have accepted,
    /// when known (otherwise <see langword="null"/>).
    /// </summary>
    public string? AcceptedKinds { get; }

    /// <summary>Creates a new <see cref="IncorrectNodeKindException"/>.</summary>
    /// <param name="node">The offending node.</param>
    /// <param name="expectedType">The name of the typed wrapper that was expected.</param>
    /// <param name="acceptedKinds">An optional description of accepted kinds.</param>
    public IncorrectNodeKindException(Node node, string expectedType, string? acceptedKinds = null)
        : base(BuildMessage(node, expectedType, acceptedKinds))
    {
        Node = node;
        ExpectedType = expectedType;
        AcceptedKinds = acceptedKinds;
    }

    private static string BuildMessage(Node node, string expectedType, string? acceptedKinds)
    {
        string actual = node.IsNull ? "(null node)" : $"'{node.Kind}'";
        string accepts = acceptedKinds is null ? string.Empty : $" (accepts {acceptedKinds})";
        return $"Node of kind {actual} cannot be represented as '{expectedType}'{accepts}.";
    }
}
