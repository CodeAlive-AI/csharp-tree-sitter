using System.Text;
using TreeSitter.Internal;
using TreeSitter.Native;

namespace TreeSitter;

/// <summary>
/// A single node in a syntax <see cref="Tree"/>. A node is a lightweight value
/// (a 32-byte handle plus a reference to its owning tree); copying it is cheap and
/// it carries no resources to dispose. A <c>default(Node)</c> value, and any node
/// for which the parser found no result, has <see cref="IsNull"/> set to
/// <see langword="true"/>.
/// </summary>
/// <remarks>
/// <para><b>Lifetime:</b> a <see cref="Node"/> is only valid while its owning
/// <see cref="Tree"/> is alive and undisposed. A node holds a reference to its tree,
/// so the tree will not be garbage-collected while a node derived from it is reachable;
/// however, it does <b>not</b> keep the tree from being explicitly
/// <see cref="Tree.Dispose">disposed</see>. The node's members make raw native calls
/// against the tree's memory with no per-call liveness guard (to avoid overhead), so
/// using a node after its tree has been disposed is <b>undefined behavior</b>. Extract
/// any data you need (e.g. <see cref="Text"/>, <see cref="Range"/>) before disposing
/// the tree, or keep the tree alive for as long as you hold its nodes.</para>
/// </remarks>
public readonly struct Node : IEquatable<Node>
{
    private readonly TSNode _node;
    private readonly Tree? _tree;

    internal Node(TSNode node, Tree? tree)
    {
        _node = node;
        _tree = tree;
    }

    internal TSNode Raw => _node;

    /// <summary>The <see cref="Tree"/> that owns this node, or <see langword="null"/> for a null node.</summary>
    internal Tree? OwningTree => _tree;

    /// <summary>
    /// <see langword="true"/> if this node is null (no underlying node). Calling
    /// most members on a null node is safe and yields empty/default results.
    /// </summary>
    public bool IsNull => _tree is null || _node.IsZero || NativeMethods.ts_node_is_null(_node);

    /// <summary>The node's type as it appears in the API, accounting for aliases (<c>ts_node_type</c>).</summary>
    public string Kind => IsNull ? string.Empty : Utf8.PtrToString(NativeMethods.ts_node_type(_node)) ?? string.Empty;

    /// <summary>The node's type as a numeric symbol id (<c>ts_node_symbol</c>).</summary>
    public ushort KindId => IsNull ? (ushort)0 : NativeMethods.ts_node_symbol(_node);

    /// <summary>
    /// The node's type as it appears in the grammar, ignoring aliases
    /// (<c>ts_node_grammar_type</c>), or <see langword="null"/> if unavailable.
    /// </summary>
    public string? GrammarKind => IsNull ? null : Utf8.PtrToString(NativeMethods.ts_node_grammar_type(_node));

    /// <summary>The node's grammar symbol id ignoring aliases (<c>ts_node_grammar_symbol</c>).</summary>
    public ushort GrammarSymbol => IsNull ? (ushort)0 : NativeMethods.ts_node_grammar_symbol(_node);

    /// <summary>Whether the node corresponds to a named rule in the grammar.</summary>
    public bool IsNamed => !IsNull && NativeMethods.ts_node_is_named(_node);

    /// <summary>Whether the node was inserted by error recovery (a missing node).</summary>
    public bool IsMissing => !IsNull && NativeMethods.ts_node_is_missing(_node);

    /// <summary>Whether the node is an extra node (e.g. a comment) not required by the grammar.</summary>
    public bool IsExtra => !IsNull && NativeMethods.ts_node_is_extra(_node);

    /// <summary>Whether the node is itself an <c>ERROR</c> node (<c>ts_node_is_error</c>).</summary>
    public bool IsError => !IsNull && NativeMethods.ts_node_is_error(_node);

    /// <summary>Whether the node has been edited since the tree was parsed.</summary>
    public bool HasChanges => !IsNull && NativeMethods.ts_node_has_changes(_node);

    /// <summary>Whether the node is, or contains, a syntax error.</summary>
    public bool HasError => !IsNull && NativeMethods.ts_node_has_error(_node);

    /// <summary>The parse state at this node's position.</summary>
    public ushort ParseState => IsNull ? (ushort)0 : NativeMethods.ts_node_parse_state(_node);

    /// <summary>The parse state immediately after this node.</summary>
    public ushort NextParseState => IsNull ? (ushort)0 : NativeMethods.ts_node_next_parse_state(_node);

    /// <summary>The byte offset of the start of the node.</summary>
    public uint StartByte => IsNull ? 0u : NativeMethods.ts_node_start_byte(_node);

    /// <summary>The byte offset one past the end of the node.</summary>
    public uint EndByte => IsNull ? 0u : NativeMethods.ts_node_end_byte(_node);

    /// <summary>The (row, column) position of the start of the node.</summary>
    public Point StartPoint => IsNull ? Point.Zero : new(NativeMethods.ts_node_start_point(_node));

    /// <summary>The (row, column) position of the end of the node.</summary>
    public Point EndPoint => IsNull ? Point.Zero : new(NativeMethods.ts_node_end_point(_node));

    /// <summary>The node's byte and point range.</summary>
    public Range Range => new(StartPoint, EndPoint, StartByte, EndByte);

    /// <summary>The number of descendants of this node, including the node itself.</summary>
    public uint DescendantCount => IsNull ? 0u : NativeMethods.ts_node_descendant_count(_node);

    /// <summary>The node's immediate parent, or a null node if this is the root.</summary>
    public Node Parent => IsNull ? default : Wrap(NativeMethods.ts_node_parent(_node));

    /// <summary>
    /// The direct child of this node that contains <paramref name="descendant"/>
    /// (which may be the descendant itself). Preferred over repeated
    /// <see cref="Parent"/> access when walking toward the root.
    /// </summary>
    /// <param name="descendant">A descendant of this node.</param>
    public Node ChildWithDescendant(Node descendant) =>
        IsNull ? default : Wrap(NativeMethods.ts_node_child_with_descendant(_node, descendant._node));

    /// <summary>The total number of children (named and anonymous).</summary>
    public uint ChildCount => IsNull ? 0u : NativeMethods.ts_node_child_count(_node);

    /// <summary>Gets the child at <paramref name="index"/>, or a null node if out of range.</summary>
    /// <param name="index">The zero-based child index.</param>
    public Node Child(uint index) => IsNull ? default : Wrap(NativeMethods.ts_node_child(_node, index));

    /// <summary>The field name of the child at <paramref name="index"/>, or <see langword="null"/>.</summary>
    /// <param name="index">The zero-based child index (over all children).</param>
    public string? FieldNameForChild(uint index) =>
        IsNull ? null : Utf8.PtrToString(NativeMethods.ts_node_field_name_for_child(_node, index));

    /// <summary>The field name of the named child at <paramref name="index"/>, or <see langword="null"/>.</summary>
    /// <param name="index">The zero-based named-child index.</param>
    public string? FieldNameForNamedChild(uint index) =>
        IsNull ? null : Utf8.PtrToString(NativeMethods.ts_node_field_name_for_named_child(_node, index));

    /// <summary>The number of named children.</summary>
    public uint NamedChildCount => IsNull ? 0u : NativeMethods.ts_node_named_child_count(_node);

    /// <summary>Gets the named child at <paramref name="index"/>, or a null node if out of range.</summary>
    /// <param name="index">The zero-based named-child index.</param>
    public Node NamedChild(uint index) => IsNull ? default : Wrap(NativeMethods.ts_node_named_child(_node, index));

    /// <summary>Gets the child with the given field name, or a null node if absent.</summary>
    /// <param name="fieldName">The field name.</param>
    public unsafe Node ChildByFieldName(string fieldName)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        if (IsNull)
            return default;
        int byteCount = Utf8.ByteCount(fieldName);
        Span<byte> buffer = byteCount <= 256 ? stackalloc byte[byteCount] : new byte[byteCount];
        Encoding.UTF8.GetBytes(fieldName, buffer);
        fixed (byte* p = buffer)
            return Wrap(NativeMethods.ts_node_child_by_field_name(_node, p, (uint)byteCount));
    }

    /// <summary>Gets the child with the given field id, or a null node if absent.</summary>
    /// <param name="fieldId">The field id.</param>
    public Node ChildByFieldId(ushort fieldId) =>
        IsNull ? default : Wrap(NativeMethods.ts_node_child_by_field_id(_node, fieldId));

    /// <summary>The node's next sibling, or a null node if there is none.</summary>
    public Node NextSibling => IsNull ? default : Wrap(NativeMethods.ts_node_next_sibling(_node));

    /// <summary>The node's previous sibling, or a null node if there is none.</summary>
    public Node PrevSibling => IsNull ? default : Wrap(NativeMethods.ts_node_prev_sibling(_node));

    /// <summary>The node's next named sibling, or a null node if there is none.</summary>
    public Node NextNamedSibling => IsNull ? default : Wrap(NativeMethods.ts_node_next_named_sibling(_node));

    /// <summary>The node's previous named sibling, or a null node if there is none.</summary>
    public Node PrevNamedSibling => IsNull ? default : Wrap(NativeMethods.ts_node_prev_named_sibling(_node));

    /// <summary>The first child that contains or starts after <paramref name="byteOffset"/>.</summary>
    /// <param name="byteOffset">A byte offset.</param>
    public Node FirstChildForByte(uint byteOffset) =>
        IsNull ? default : Wrap(NativeMethods.ts_node_first_child_for_byte(_node, byteOffset));

    /// <summary>The first named child that contains or starts after <paramref name="byteOffset"/>.</summary>
    /// <param name="byteOffset">A byte offset.</param>
    public Node FirstNamedChildForByte(uint byteOffset) =>
        IsNull ? default : Wrap(NativeMethods.ts_node_first_named_child_for_byte(_node, byteOffset));

    /// <summary>The smallest node spanning the given byte range.</summary>
    /// <param name="startByte">The start byte offset (inclusive).</param>
    /// <param name="endByte">The end byte offset (exclusive).</param>
    public Node DescendantForByteRange(uint startByte, uint endByte) =>
        IsNull ? default : Wrap(NativeMethods.ts_node_descendant_for_byte_range(_node, startByte, endByte));

    /// <summary>The smallest node spanning the given point range.</summary>
    /// <param name="start">The start position.</param>
    /// <param name="end">The end position.</param>
    public Node DescendantForPointRange(Point start, Point end) =>
        IsNull ? default : Wrap(NativeMethods.ts_node_descendant_for_point_range(_node, start.ToNative(), end.ToNative()));

    /// <summary>The smallest named node spanning the given byte range.</summary>
    /// <param name="startByte">The start byte offset (inclusive).</param>
    /// <param name="endByte">The end byte offset (exclusive).</param>
    public Node NamedDescendantForByteRange(uint startByte, uint endByte) =>
        IsNull ? default : Wrap(NativeMethods.ts_node_named_descendant_for_byte_range(_node, startByte, endByte));

    /// <summary>The smallest named node spanning the given point range.</summary>
    /// <param name="start">The start position.</param>
    /// <param name="end">The end position.</param>
    public Node NamedDescendantForPointRange(Point start, Point end) =>
        IsNull ? default : Wrap(NativeMethods.ts_node_named_descendant_for_point_range(_node, start.ToNative(), end.ToNative()));

    /// <summary>
    /// Updates this node's stored position to reflect an edit. Because
    /// <see cref="Node"/> is immutable, this returns a new, edited node rather than
    /// mutating in place; the underlying tree should also be edited via
    /// <see cref="Tree.Edit(in InputEdit)"/>.
    /// </summary>
    /// <param name="edit">The edit to apply.</param>
    public unsafe Node Edit(in InputEdit edit)
    {
        if (IsNull)
            return this;
        TSNode copy = _node;
        TSInputEdit native = edit.ToNative();
        NativeMethods.ts_node_edit(&copy, &native);
        return new Node(copy, _tree);
    }

    /// <summary>The node's <see cref="Language"/>, or <see langword="null"/> for a null node.</summary>
    public Language? Language
    {
        get
        {
            if (IsNull)
                return null;
            IntPtr ptr = NativeMethods.ts_node_language(_node);
            return ptr == IntPtr.Zero ? null : _tree?.Language ?? new Language(ptr);
        }
    }

    /// <summary>
    /// The slice of the owning tree's UTF-8 source covered by this node. Empty if
    /// the node is null or the tree has no retained source.
    /// </summary>
    public ReadOnlySpan<byte> TextSpan
    {
        get
        {
            if (_tree is null)
                return ReadOnlySpan<byte>.Empty;

            ReadOnlySpan<byte> source = _tree.Source.Span;
            uint start = StartByte;
            uint end = EndByte;
            if (end <= start || start >= (uint)source.Length)
                return ReadOnlySpan<byte>.Empty;
            if (end > (uint)source.Length)
                end = (uint)source.Length;
            return source.Slice((int)start, (int)(end - start));
        }
    }

    /// <summary>
    /// The source text covered by this node, decoded from the owning tree's UTF-8
    /// source. Empty if the node is null or the tree has no retained source.
    /// </summary>
    public string Text => Encoding.UTF8.GetString(TextSpan);

    /// <summary>
    /// Returns an S-expression describing the subtree rooted at this node
    /// (<c>ts_node_string</c>). The native buffer is freed before returning.
    /// </summary>
    public string ToSExpression()
    {
        if (IsNull)
            return string.Empty;
        IntPtr ptr = NativeMethods.ts_node_string(_node);
        if (ptr == IntPtr.Zero)
            return string.Empty;
        try
        {
            return Utf8.PtrToString(ptr) ?? string.Empty;
        }
        finally
        {
            Libc.Free(ptr);
        }
    }

    /// <summary>Lazily enumerates all children (named and anonymous) of this node.</summary>
    public IEnumerable<Node> Children
    {
        get
        {
            uint count = ChildCount;
            for (uint i = 0; i < count; i++)
                yield return Child(i);
        }
    }

    /// <summary>Lazily enumerates the named children of this node.</summary>
    public IEnumerable<Node> NamedChildren
    {
        get
        {
            uint count = NamedChildCount;
            for (uint i = 0; i < count; i++)
                yield return NamedChild(i);
        }
    }

    /// <summary>Creates a <see cref="TreeCursor"/> rooted at this node.</summary>
    public TreeCursor Walk() => new(this);

    /// <summary>Determines whether this node is identical to <paramref name="other"/>.</summary>
    /// <param name="other">The node to compare against.</param>
    public bool Equals(Node other)
    {
        // Two null nodes are equal; otherwise defer to ts_node_eq which compares id+tree.
        bool thisNull = _tree is null || _node.IsZero;
        bool otherNull = other._tree is null || other._node.IsZero;
        if (thisNull || otherNull)
            return thisNull && otherNull;
        return NativeMethods.ts_node_eq(_node, other._node);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Node other && Equals(other);

    /// <inheritdoc/>
    /// <remarks>
    /// All null nodes hash to <c>0</c> so that a tree-attached null node (e.g.
    /// <see cref="Parent"/> of the root, or an absent <see cref="ChildByFieldName(string)"/>)
    /// and <c>default(Node)</c> — which <see cref="Equals(Node)"/> reports as equal —
    /// also have equal hash codes, honouring the equality/hash contract.
    /// </remarks>
    public override int GetHashCode() => IsNull ? 0 : _node.GetHashCode();

    /// <summary>Determines whether two nodes are identical.</summary>
    public static bool operator ==(Node left, Node right) => left.Equals(right);

    /// <summary>Determines whether two nodes differ.</summary>
    public static bool operator !=(Node left, Node right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => IsNull ? "(null)" : ToSExpression();

    /// <summary>Wraps a raw node from the native API in the current tree context.</summary>
    private Node Wrap(TSNode raw) => new(raw, _tree);
}
