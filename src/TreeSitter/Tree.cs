using TreeSitter.Native;

namespace TreeSitter;

/// <summary>
/// A syntax tree produced by a <see cref="Parser"/>. A tree owns native memory and
/// must be disposed. It retains the UTF-8 source bytes it was parsed from so that
/// <see cref="Node.Text"/> and friends can slice them.
/// </summary>
/// <remarks>
/// Trees are not thread-safe; use <see cref="Copy"/> to obtain an independent tree
/// for use on another thread.
/// </remarks>
public sealed class Tree : IDisposable
{
    private IntPtr _handle;
    private readonly ReadOnlyMemory<byte> _source;
    private readonly Language _language;

    internal Tree(IntPtr handle, ReadOnlyMemory<byte> source, Language language)
    {
        _handle = handle;
        _source = source;
        _language = language;
    }

    /// <summary>Frees the native tree if it was not explicitly disposed.</summary>
    ~Tree() => DisposeCore();

    internal IntPtr Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
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
        IntPtr copy = NativeMethods.ts_tree_copy(Handle);
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

    /// <inheritdoc/>
    public void Dispose()
    {
        DisposeCore();
        GC.SuppressFinalize(this);
    }

    private void DisposeCore()
    {
        IntPtr handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle != IntPtr.Zero)
            NativeMethods.ts_tree_delete(handle);
    }
}
