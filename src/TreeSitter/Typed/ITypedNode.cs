namespace TreeSitter.Typed;

/// <summary>
/// The non-generic base of every strongly-typed node. A typed node is a thin,
/// zero-allocation wrapper around a <see cref="TreeSitter.Node"/> whose existence
/// guarantees an invariant about the underlying node's <see cref="TreeSitter.Node.Kind"/>.
/// </summary>
public interface ITypedNode
{
    /// <summary>The underlying untyped <see cref="TreeSitter.Node"/>.</summary>
    Node Node { get; }
}

/// <summary>
/// A strongly-typed node that knows how to validate and construct itself from an
/// untyped <see cref="TreeSitter.Node"/>. Implementations are <see langword="readonly"/>
/// <see langword="struct"/>s, so values are cheap to copy and never allocate.
/// </summary>
/// <typeparam name="TSelf">The implementing type (curiously-recurring pattern).</typeparam>
public interface ITypedNode<TSelf> : ITypedNode
    where TSelf : struct, ITypedNode<TSelf>
{
    /// <summary>
    /// Determines whether a node of the given <paramref name="kind"/> can be
    /// represented by <typeparamref name="TSelf"/>. For a concrete node this is a
    /// single-kind comparison; for a supertype it is membership in the (transitively
    /// flattened) set of accepted concrete kinds.
    /// </summary>
    /// <param name="kind">A node kind string (<see cref="TreeSitter.Node.Kind"/>).</param>
    static abstract bool Accepts(string kind);

    /// <summary>
    /// Attempts to wrap <paramref name="node"/> as <typeparamref name="TSelf"/>,
    /// returning <see langword="null"/> when the node is null or its kind is not
    /// accepted. This is the safe, validating constructor.
    /// </summary>
    /// <param name="node">The node to wrap.</param>
    static abstract TSelf? TryFrom(Node node);

    /// <summary>
    /// Wraps <paramref name="node"/> as <typeparamref name="TSelf"/> without
    /// validation. The caller must have already established that the node's kind is
    /// accepted (a <c>Debug.Assert</c> guards this in debug builds). Used by
    /// generated dispatch code after a kind switch.
    /// </summary>
    /// <param name="node">The already-validated node to wrap.</param>
    static abstract TSelf FromUnchecked(Node node);
}
