namespace TreeSitter.Typed;

/// <summary>
/// A typed wrapper that imposes no kind constraint: it holds an arbitrary
/// <see cref="TreeSitter.Node"/> and offers on-demand, checked conversions to
/// concrete typed wrappers. Used where the grammar permits any node, or as an
/// escape hatch from generated typed accessors.
/// </summary>
public readonly struct UntypedNode : ITypedNode<UntypedNode>
{
    /// <summary>Creates an <see cref="UntypedNode"/> wrapping <paramref name="node"/>.</summary>
    /// <param name="node">The node to wrap.</param>
    public UntypedNode(Node node) => Node = node;

    /// <inheritdoc/>
    public Node Node { get; }

    /// <summary>Determines whether <paramref name="kind"/> is acceptable; always <see langword="true"/>.</summary>
    /// <param name="kind">A node kind string (ignored).</param>
    public static bool Accepts(string kind) => true;

    /// <summary>
    /// Wraps <paramref name="node"/>, returning <see langword="null"/> only when the
    /// node itself is null.
    /// </summary>
    /// <param name="node">The node to wrap.</param>
    public static UntypedNode? TryFrom(Node node) => node.IsNull ? null : new UntypedNode(node);

    /// <summary>Wraps <paramref name="node"/> without validation.</summary>
    /// <param name="node">The node to wrap.</param>
    public static UntypedNode FromUnchecked(Node node) => new(node);

    /// <summary>Determines whether the wrapped node can be represented as <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">A typed node wrapper.</typeparam>
    public bool Is<T>() where T : struct, ITypedNode<T> => !Node.IsNull && T.Accepts(Node.Kind);

    /// <summary>
    /// Attempts to convert the wrapped node to <typeparamref name="T"/>, returning
    /// <see langword="null"/> when its kind is not accepted.
    /// </summary>
    /// <typeparam name="T">A typed node wrapper.</typeparam>
    public T? As<T>() where T : struct, ITypedNode<T> => T.TryFrom(Node);

    /// <summary>
    /// Converts the wrapped node to <typeparamref name="T"/>, throwing
    /// <see cref="IncorrectNodeKindException"/> when its kind is not accepted.
    /// </summary>
    /// <typeparam name="T">A typed node wrapper.</typeparam>
    /// <exception cref="IncorrectNodeKindException">The node's kind is not accepted by <typeparamref name="T"/>.</exception>
    public T Cast<T>() where T : struct, ITypedNode<T> =>
        T.TryFrom(Node) ?? throw new IncorrectNodeKindException(Node, typeof(T).Name);
}

/// <summary>
/// A typed wrapper that asserts the wrapped node is an <em>extra</em> node (e.g. a
/// comment), i.e. one not required by the grammar (<see cref="TreeSitter.Node.IsExtra"/>).
/// </summary>
public readonly struct ExtraNode : ITypedNode<ExtraNode>
{
    /// <summary>Wraps <paramref name="node"/> without any check. The caller guarantees extra-ness.</summary>
    /// <param name="node">An extra node.</param>
    private ExtraNode(Node node) => Node = node;

    /// <inheritdoc/>
    public Node Node { get; }

    /// <summary>Always <see langword="false"/>: extra-ness is not determined by kind alone.</summary>
    /// <param name="kind">A node kind string (ignored).</param>
    public static bool Accepts(string kind) => false;

    /// <summary>Wraps <paramref name="node"/> if it is an extra node, else <see langword="null"/>.</summary>
    /// <param name="node">The node to wrap.</param>
    public static ExtraNode? TryFrom(Node node) =>
        !node.IsNull && node.IsExtra ? new ExtraNode(node) : null;

    /// <summary>
    /// Wraps <paramref name="node"/>, throwing if it is not an extra node. Unlike a
    /// kind-checked typed node, extra-ness is a runtime property, so this throwing
    /// factory is the checked construction surface (in all build configurations).
    /// </summary>
    /// <param name="node">The node to wrap.</param>
    /// <exception cref="IncorrectNodeKindException"><paramref name="node"/> is null or not an extra node.</exception>
    public static ExtraNode Wrap(Node node) =>
        TryFrom(node) ?? throw new IncorrectNodeKindException(node, nameof(ExtraNode), "an extra node");

    /// <summary>Wraps <paramref name="node"/> without validation.</summary>
    /// <param name="node">The node to wrap.</param>
    public static ExtraNode FromUnchecked(Node node) => new(node);
}

/// <summary>
/// A typed wrapper that asserts the wrapped node is, or contains, a syntax
/// <c>ERROR</c> (<see cref="TreeSitter.Node.IsError"/>).
/// </summary>
public readonly struct ErrorNode : ITypedNode<ErrorNode>
{
    /// <summary>Wraps <paramref name="node"/> without any check. The caller guarantees error-ness.</summary>
    /// <param name="node">An error node.</param>
    private ErrorNode(Node node) => Node = node;

    /// <inheritdoc/>
    public Node Node { get; }

    /// <summary>Always <see langword="false"/>: error-ness is not determined by kind alone.</summary>
    /// <param name="kind">A node kind string (ignored).</param>
    public static bool Accepts(string kind) => false;

    /// <summary>Wraps <paramref name="node"/> if it is an error node, else <see langword="null"/>.</summary>
    /// <param name="node">The node to wrap.</param>
    public static ErrorNode? TryFrom(Node node) =>
        !node.IsNull && node.IsError ? new ErrorNode(node) : null;

    /// <summary>
    /// Wraps <paramref name="node"/>, throwing if it is not an error node. Unlike a
    /// kind-checked typed node, error-ness is a runtime property, so this throwing
    /// factory is the checked construction surface (in all build configurations).
    /// </summary>
    /// <param name="node">The node to wrap.</param>
    /// <exception cref="IncorrectNodeKindException"><paramref name="node"/> is null or not an error node.</exception>
    public static ErrorNode Wrap(Node node) =>
        TryFrom(node) ?? throw new IncorrectNodeKindException(node, nameof(ErrorNode), "an error node");

    /// <summary>Wraps <paramref name="node"/> without validation.</summary>
    /// <param name="node">The node to wrap.</param>
    public static ErrorNode FromUnchecked(Node node) => new(node);
}

/// <summary>
/// A typed wrapper that asserts the wrapped node is a <em>missing</em> node inserted
/// by error recovery (<see cref="TreeSitter.Node.IsMissing"/>).
/// </summary>
public readonly struct MissingNode : ITypedNode<MissingNode>
{
    /// <summary>Wraps <paramref name="node"/> without any check. The caller guarantees missing-ness.</summary>
    /// <param name="node">A missing node.</param>
    private MissingNode(Node node) => Node = node;

    /// <inheritdoc/>
    public Node Node { get; }

    /// <summary>Always <see langword="false"/>: missing-ness is not determined by kind alone.</summary>
    /// <param name="kind">A node kind string (ignored).</param>
    public static bool Accepts(string kind) => false;

    /// <summary>Wraps <paramref name="node"/> if it is a missing node, else <see langword="null"/>.</summary>
    /// <param name="node">The node to wrap.</param>
    public static MissingNode? TryFrom(Node node) =>
        !node.IsNull && node.IsMissing ? new MissingNode(node) : null;

    /// <summary>
    /// Wraps <paramref name="node"/>, throwing if it is not a missing node. Unlike a
    /// kind-checked typed node, missing-ness is a runtime property, so this throwing
    /// factory is the checked construction surface (in all build configurations).
    /// </summary>
    /// <param name="node">The node to wrap.</param>
    /// <exception cref="IncorrectNodeKindException"><paramref name="node"/> is null or not a missing node.</exception>
    public static MissingNode Wrap(Node node) =>
        TryFrom(node) ?? throw new IncorrectNodeKindException(node, nameof(MissingNode), "a missing node");

    /// <summary>Wraps <paramref name="node"/> without validation.</summary>
    /// <param name="node">The node to wrap.</param>
    public static MissingNode FromUnchecked(Node node) => new(node);
}
