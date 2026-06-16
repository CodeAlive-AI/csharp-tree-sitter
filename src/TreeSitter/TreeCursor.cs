using TreeSitter.Internal;
using TreeSitter.Native;

namespace TreeSitter;

/// <summary>
/// A stateful cursor for walking a syntax <see cref="Tree"/> efficiently. The
/// cursor is rooted at the node it is created from and cannot move outside it.
/// Owns native memory and must be disposed.
/// </summary>
public sealed class TreeCursor : IDisposable
{
    private TSTreeCursor _cursor;
    private readonly Tree? _tree;
    private bool _disposed;

    /// <summary>Creates a cursor rooted at <paramref name="node"/>.</summary>
    /// <param name="node">The node to root the cursor at.</param>
    public TreeCursor(Node node)
    {
        _cursor = NativeMethods.ts_tree_cursor_new(node.Raw);
        _tree = node.OwningTree;
    }

    // Used by Copy() to wrap an already-created native cursor sharing the same tree.
    private TreeCursor(TSTreeCursor cursor, Tree? tree)
    {
        _cursor = cursor;
        _tree = tree;
    }

    /// <summary>Frees the native cursor if it was not explicitly disposed.</summary>
    ~TreeCursor() => DisposeCore();

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>The node the cursor is currently positioned on.</summary>
    public unsafe Node Current
    {
        get
        {
            ThrowIfDisposed();
            fixed (TSTreeCursor* c = &_cursor)
                return new Node(NativeMethods.ts_tree_cursor_current_node(c), _tree);
        }
    }

    /// <summary>The field name of the current node, or <see langword="null"/> if it has none.</summary>
    public unsafe string? CurrentFieldName
    {
        get
        {
            ThrowIfDisposed();
            fixed (TSTreeCursor* c = &_cursor)
                return Utf8.PtrToString(NativeMethods.ts_tree_cursor_current_field_name(c));
        }
    }

    /// <summary>The field id of the current node, or <c>0</c> if it has none.</summary>
    public unsafe ushort CurrentFieldId
    {
        get
        {
            ThrowIfDisposed();
            fixed (TSTreeCursor* c = &_cursor)
                return NativeMethods.ts_tree_cursor_current_field_id(c);
        }
    }

    /// <summary>The depth of the current node relative to the cursor's root (0 = root).</summary>
    public unsafe uint CurrentDepth
    {
        get
        {
            ThrowIfDisposed();
            fixed (TSTreeCursor* c = &_cursor)
                return NativeMethods.ts_tree_cursor_current_depth(c);
        }
    }

    /// <summary>The preorder index of the current node among the cursor root's descendants.</summary>
    public unsafe uint CurrentDescendantIndex
    {
        get
        {
            ThrowIfDisposed();
            fixed (TSTreeCursor* c = &_cursor)
                return NativeMethods.ts_tree_cursor_current_descendant_index(c);
        }
    }

    /// <summary>Moves to the parent of the current node. Returns whether it moved.</summary>
    public unsafe bool GotoParent()
    {
        ThrowIfDisposed();
        fixed (TSTreeCursor* c = &_cursor)
            return NativeMethods.ts_tree_cursor_goto_parent(c);
    }

    /// <summary>Moves to the first child of the current node. Returns whether it moved.</summary>
    public unsafe bool GotoFirstChild()
    {
        ThrowIfDisposed();
        fixed (TSTreeCursor* c = &_cursor)
            return NativeMethods.ts_tree_cursor_goto_first_child(c);
    }

    /// <summary>Moves to the last child of the current node. Returns whether it moved.</summary>
    public unsafe bool GotoLastChild()
    {
        ThrowIfDisposed();
        fixed (TSTreeCursor* c = &_cursor)
            return NativeMethods.ts_tree_cursor_goto_last_child(c);
    }

    /// <summary>Moves to the next sibling of the current node. Returns whether it moved.</summary>
    public unsafe bool GotoNextSibling()
    {
        ThrowIfDisposed();
        fixed (TSTreeCursor* c = &_cursor)
            return NativeMethods.ts_tree_cursor_goto_next_sibling(c);
    }

    /// <summary>Moves to the previous sibling of the current node. Returns whether it moved.</summary>
    public unsafe bool GotoPreviousSibling()
    {
        ThrowIfDisposed();
        fixed (TSTreeCursor* c = &_cursor)
            return NativeMethods.ts_tree_cursor_goto_previous_sibling(c);
    }

    /// <summary>
    /// Moves to the first child that contains or starts after <paramref name="byteOffset"/>.
    /// </summary>
    /// <param name="byteOffset">A byte offset.</param>
    /// <returns>The child index, or <c>-1</c> if no such child exists.</returns>
    public unsafe long GotoFirstChildForByte(uint byteOffset)
    {
        ThrowIfDisposed();
        fixed (TSTreeCursor* c = &_cursor)
            return NativeMethods.ts_tree_cursor_goto_first_child_for_byte(c, byteOffset);
    }

    /// <summary>
    /// Moves to the first child that contains or starts after <paramref name="point"/>.
    /// </summary>
    /// <param name="point">A position.</param>
    /// <returns>The child index, or <c>-1</c> if no such child exists.</returns>
    public unsafe long GotoFirstChildForPoint(Point point)
    {
        ThrowIfDisposed();
        fixed (TSTreeCursor* c = &_cursor)
            return NativeMethods.ts_tree_cursor_goto_first_child_for_point(c, point.ToNative());
    }

    /// <summary>Jumps to the nth descendant of the cursor's root (0 = root).</summary>
    /// <param name="descendantIndex">The descendant index in preorder.</param>
    public unsafe void GotoDescendant(uint descendantIndex)
    {
        ThrowIfDisposed();
        fixed (TSTreeCursor* c = &_cursor)
            NativeMethods.ts_tree_cursor_goto_descendant(c, descendantIndex);
    }

    /// <summary>Re-roots the cursor at <paramref name="node"/>, losing parent context.</summary>
    /// <param name="node">The new root node.</param>
    public unsafe void Reset(Node node)
    {
        ThrowIfDisposed();
        fixed (TSTreeCursor* c = &_cursor)
            NativeMethods.ts_tree_cursor_reset(c, node.Raw);
    }

    /// <summary>
    /// Re-initializes this cursor to the same position as <paramref name="other"/>,
    /// preserving parent context and reusing this cursor's allocation.
    /// </summary>
    /// <param name="other">The cursor whose position to copy.</param>
    public unsafe void ResetTo(TreeCursor other)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(other);
        other.ThrowIfDisposed();
        fixed (TSTreeCursor* dst = &_cursor)
        fixed (TSTreeCursor* src = &other._cursor)
            NativeMethods.ts_tree_cursor_reset_to(dst, src);
    }

    /// <summary>Creates an independent copy of this cursor that must be disposed separately.</summary>
    public unsafe TreeCursor Copy()
    {
        ThrowIfDisposed();
        fixed (TSTreeCursor* c = &_cursor)
            return new TreeCursor(NativeMethods.ts_tree_cursor_copy(c), _tree);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        DisposeCore();
        GC.SuppressFinalize(this);
    }

    private unsafe void DisposeCore()
    {
        if (_disposed)
            return;
        _disposed = true;
        fixed (TSTreeCursor* c = &_cursor)
            NativeMethods.ts_tree_cursor_delete(c);
    }
}
