using System.Runtime.InteropServices;

namespace TreeSitter.Native;

// =============================================================================
// SafeHandle wrappers for the owned native objects in tree-sitter's API.
//
// Why SafeHandle (vs. raw IntPtr + finalizer + Interlocked.Exchange)?
//   * The runtime ref-counts a SafeHandle around every P/Invoke that takes it,
//     so the native object cannot be released (finalized/disposed) while a call
//     using it is still in flight. This closes the finalizer/P-Invoke race that
//     the manual IntPtr pattern is exposed to.
//   * ReleaseHandle() runs as a critical finalizer, guaranteed to execute even
//     under abnormal shutdown, so native memory is reliably reclaimed.
//   * [LibraryImport] marshals SafeHandle parameters and returns directly: the
//     source generator emits the AddRef/Release (for parameters) and the
//     allocate-then-SetHandle dance (for returns) for us.
//
// Ownership rules encoded here:
//   * Each subclass owns exactly one TS* object and releases it via the matching
//     ts_*_delete in ReleaseHandle().
//   * Returned-as-SafeHandle creators (ts_parser_new, ts_tree_*, ts_query_new,
//     ts_query_cursor_new, ts_lookahead_iterator_*) hand the marshaller a fresh,
//     OWNING handle. A null return surfaces as IsInvalid == true.
//   * BORROWED pointers (e.g. old_tree in parse, the TSTree* in ts_tree_root_node,
//     the query in ts_query_cursor_exec) are passed as the owner's SafeHandle so
//     the marshaller keeps it alive across the call without transferring ownership.
//
// Value types (TSNode, TSTreeCursor, TSQueryMatch, ...) are NOT wrapped here:
// they carry no ownership or are released by explicit by-ref calls.
// =============================================================================

/// <summary>
/// Base class for a SafeHandle that owns a pointer allocated by tree-sitter.
/// The handle is considered invalid when it is the null pointer.
/// </summary>
internal abstract class TreeSitterHandle : SafeHandle
{
    /// <summary>
    /// Initializes an owning handle with an invalid (null) value. The marshaller
    /// (or a factory) populates the real pointer via <see cref="SafeHandle.SetHandle"/>.
    /// </summary>
    protected TreeSitterHandle() : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    /// <inheritdoc/>
    public override bool IsInvalid => handle == IntPtr.Zero;
}

/// <summary>Owns a <c>TSParser*</c>; releases it with <c>ts_parser_delete</c>.</summary>
internal sealed class ParserHandle : TreeSitterHandle
{
    /// <summary>Allocates a new parser, throwing if the native allocation fails.</summary>
    internal static ParserHandle Create()
    {
        ParserHandle handle = NativeMethods.ts_parser_new();
        if (handle.IsInvalid)
            throw new TreeSitterException("Failed to allocate a tree-sitter parser.");
        return handle;
    }

    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        NativeMethods.ts_parser_delete(handle);
        return true;
    }
}

/// <summary>Owns a <c>TSTree*</c>; releases it with <c>ts_tree_delete</c>.</summary>
internal sealed class TreeHandle : TreeSitterHandle
{
    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        NativeMethods.ts_tree_delete(handle);
        return true;
    }
}

/// <summary>Owns a <c>TSQuery*</c>; releases it with <c>ts_query_delete</c>.</summary>
internal sealed class QueryHandle : TreeSitterHandle
{
    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        NativeMethods.ts_query_delete(handle);
        return true;
    }
}

/// <summary>Owns a <c>TSQueryCursor*</c>; releases it with <c>ts_query_cursor_delete</c>.</summary>
internal sealed class QueryCursorHandle : TreeSitterHandle
{
    /// <summary>Allocates a new query cursor, throwing if the native allocation fails.</summary>
    internal static QueryCursorHandle Create()
    {
        QueryCursorHandle handle = NativeMethods.ts_query_cursor_new();
        if (handle.IsInvalid)
            throw new TreeSitterException("Failed to allocate a tree-sitter query cursor.");
        return handle;
    }

    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        NativeMethods.ts_query_cursor_delete(handle);
        return true;
    }
}

/// <summary>Owns a <c>TSLookaheadIterator*</c>; releases it with <c>ts_lookahead_iterator_delete</c>.</summary>
internal sealed class LookaheadIteratorHandle : TreeSitterHandle
{
    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        NativeMethods.ts_lookahead_iterator_delete(handle);
        return true;
    }
}
