using TreeSitter.Native;

namespace TreeSitter;

/// <summary>
/// A syntax tree produced by a <see cref="Parser"/>. A tree owns native memory and
/// must be disposed. It retains the UTF-8 source bytes it was parsed from so that
/// <see cref="Node.Text"/> and friends can slice them.
/// </summary>
/// <remarks>
/// Trees are not thread-safe; use <see cref="Copy"/> to obtain an independent tree
/// for use on another thread. The native <c>TSTree*</c> is held in a
/// <see cref="TreeHandle"/> (a <see cref="System.Runtime.InteropServices.SafeHandle"/>),
/// so disposal is idempotent and the handle cannot be freed while a P/Invoke using
/// it is in flight.
/// <para><b>Node lifetime:</b> a <see cref="Node"/> obtained from this tree (directly
/// or via navigation, queries, or cursors) is only valid while the tree is alive and
/// undisposed. Using such a node after the tree has been disposed is undefined
/// behavior; extract any needed data before disposing, or keep the tree alive for as
/// long as its nodes are in use. See <see cref="Node"/>.</para>
/// </remarks>
public sealed class Tree : IDisposable
{
    private readonly TreeHandle _handle;
    private readonly ReadOnlyMemory<byte> _source;
    private readonly Language _language;

    internal Tree(TreeHandle handle, ReadOnlyMemory<byte> source, Language language)
    {
        _handle = handle;
        _source = source;
        _language = language;
    }

    /// <summary>
    /// The owning <see cref="TreeHandle"/>, validated against disposal. Passed to
    /// P/Invoke as a borrowed reference; the marshaller ref-counts it across calls.
    /// </summary>
    internal TreeHandle Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_handle.IsClosed || _handle.IsInvalid, this);
            return _handle;
        }
    }

    /// <summary>The UTF-8 source bytes this tree was parsed from.</summary>
    public ReadOnlyMemory<byte> Source => _source;

    /// <summary>The language used to parse this tree.</summary>
    public Language Language => _language;

    /// <summary>The root node of the tree.</summary>
    public Node RootNode => new(NativeMethods.ts_tree_root_node(Handle), this);

    /// <summary>
    /// The root node of the tree with its position shifted forward by the given
    /// offset, useful when this tree represents an embedded sub-document.
    /// </summary>
    /// <param name="offsetBytes">The byte offset to add to all positions.</param>
    /// <param name="offsetPoint">The point offset to add to all positions.</param>
    public Node RootNodeWithOffset(uint offsetBytes, Point offsetPoint) =>
        new(NativeMethods.ts_tree_root_node_with_offset(Handle, offsetBytes, offsetPoint.ToNative()), this);

    /// <summary>
    /// Creates a shallow copy of the tree (fast). The copy shares the same retained
    /// source bytes and language, and must be disposed independently.
    /// </summary>
    public Tree Copy()
    {
        TreeHandle copy = NativeMethods.ts_tree_copy(Handle);
        return new Tree(copy, _source, _language);
    }

    /// <summary>
    /// Applies an edit to the tree so it can be reused for incremental re-parsing.
    /// You are responsible for supplying the corresponding edited source to the
    /// next parse.
    /// </summary>
    /// <param name="edit">The edit to apply.</param>
    public unsafe void Edit(in InputEdit edit)
    {
        TSInputEdit native = edit.ToNative();
        NativeMethods.ts_tree_edit(Handle, &native);
    }

    /// <summary>
    /// Computes the ranges whose syntactic structure differs between an older
    /// version of this document and this tree. Pass the tree that was edited and
    /// re-parsed to produce this one.
    /// </summary>
    /// <param name="oldTree">The previous (edited) tree for the same document.</param>
    /// <returns>The changed ranges, possibly empty.</returns>
    public unsafe Range[] GetChangedRanges(Tree oldTree)
    {
        ArgumentNullException.ThrowIfNull(oldTree);
        uint length;
        TSRange* ptr = NativeMethods.ts_tree_get_changed_ranges(oldTree.Handle, Handle, &length);
        return CopyAndFreeRanges(ptr, length);
    }

    /// <summary>The list of source ranges that were included when parsing this tree.</summary>
    public unsafe Range[] IncludedRanges
    {
        get
        {
            uint length;
            TSRange* ptr = NativeMethods.ts_tree_included_ranges(Handle, &length);
            return CopyAndFreeRanges(ptr, length);
        }
    }

    /// <summary>
    /// Copies a malloc'd <c>TSRange</c> array into a managed array and frees the
    /// native buffer with libc <c>free</c>, as required by the C API ownership rules.
    /// </summary>
    private static unsafe Range[] CopyAndFreeRanges(TSRange* ptr, uint length)
    {
        if (ptr is null || length == 0)
        {
            if (ptr is not null)
                Libc.Free((IntPtr)ptr);
            return [];
        }

        try
        {
            var result = new Range[length];
            for (uint i = 0; i < length; i++)
                result[i] = new Range(ptr[i]);
            return result;
        }
        finally
        {
            Libc.Free((IntPtr)ptr);
        }
    }

    /// <summary>
    /// Disposes the underlying native tree. Idempotent: the <see cref="TreeHandle"/>
    /// owns the critical finalizer and guards against double-free.
    /// </summary>
    /// <remarks>
    /// After disposal, every <see cref="Node"/> previously obtained from this tree is
    /// invalid: its members read freed native memory, which is undefined behavior. Do
    /// not retain or use nodes past the disposal of their owning tree.
    /// </remarks>
    public void Dispose() => _handle.Dispose();
}
