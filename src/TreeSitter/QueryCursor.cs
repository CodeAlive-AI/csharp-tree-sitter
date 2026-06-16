using TreeSitter.Native;

namespace TreeSitter;

/// <summary>
/// Executes a <see cref="Query"/> against a node and iterates the resulting matches
/// or captures. Owns native memory and must be disposed. A single cursor can be
/// reused to run successive queries.
/// </summary>
public sealed class QueryCursor : IDisposable
{
    private readonly QueryCursorHandle _handle;

    // The tree whose nodes the most recent Exec() operates on; used to wrap nodes
    // returned in matches.
    private Tree? _tree;

    // The query passed to the most recent Exec(). Rooted here so that the managed
    // Query (and its QueryHandle) cannot be finalized while matches/captures are
    // being consumed, since the native cursor keeps using the query's native state.
    private Query? _query;

    /// <summary>Creates a new query cursor.</summary>
    public QueryCursor()
    {
        _handle = QueryCursorHandle.Create();
    }

    /// <summary>
    /// Frees the native auxiliary cells if the cursor was not disposed. The native
    /// cursor itself is reclaimed by <see cref="QueryCursorHandle"/>'s critical
    /// finalizer; this only releases the auxiliary unmanaged allocations.
    /// </summary>
    ~QueryCursor() => FreeNativeCells();

    private QueryCursorHandle Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_handle.IsClosed || _handle.IsInvalid, this);
            return _handle;
        }
    }

    /// <summary>
    /// Starts executing <paramref name="query"/> against the subtree rooted at
    /// <paramref name="node"/>.
    /// </summary>
    /// <param name="query">The query to run.</param>
    /// <param name="node">The node to run it against.</param>
    /// <remarks>
    /// The <paramref name="query"/> and the owning <see cref="Tree"/> of
    /// <paramref name="node"/> must outlive iteration: the native cursor retains
    /// references to the query and the tree's nodes, and (for timed execution) to a
    /// progress-callback options block, all of which are dereferenced by subsequent
    /// <see cref="NextMatch"/>/<see cref="NextCapture"/> calls. This cursor roots the
    /// query and tree internally for the duration of the run; callers must still keep
    /// the <see cref="Tree"/> undisposed.
    /// </remarks>
    public unsafe void Exec(Query query, Node node)
    {
        ArgumentNullException.ThrowIfNull(query);
        _tree = node.OwningTree;
        // Root the query so neither it nor its QueryHandle can be GC-finalized while
        // the native cursor still references the query's state during iteration.
        _query = query;

        if (_timeoutMicros == 0)
        {
            NativeMethods.ts_query_cursor_exec(Handle, query.Handle, node.Raw);
            return;
        }

        // Enforce the configured timeout via the progress callback, which fires during
        // subsequent NextMatch/NextCapture calls. Unlike ts_parser_parse_with_options
        // (which copies the options by value), ts_query_cursor_exec_with_options STORES
        // the options pointer and dereferences it on every later NextMatch/NextCapture.
        // The options block AND the deadline it points at must therefore outlive Exec();
        // both live in native cells owned by this cursor (freed on Dispose) so the
        // retained pointer stays valid for the whole exec->iterate window.
        if (_deadlineCell is null)
            _deadlineCell = (long*)System.Runtime.InteropServices.NativeMemory.Alloc((nuint)sizeof(long));
        if (_optionsCell is null)
            _optionsCell = (TSQueryCursorOptions*)System.Runtime.InteropServices.NativeMemory.Alloc(
                (nuint)sizeof(TSQueryCursorOptions));

        *_deadlineCell = System.Diagnostics.Stopwatch.GetTimestamp() +
            (long)((double)_timeoutMicros / 1_000_000.0 * System.Diagnostics.Stopwatch.Frequency);
        _optionsCell->Payload = (IntPtr)_deadlineCell;
        _optionsCell->ProgressCallback = &QueryProgressThunk;

        NativeMethods.ts_query_cursor_exec_with_options(Handle, query.Handle, node.Raw, _optionsCell);
    }

    private unsafe long* _deadlineCell;
    private unsafe TSQueryCursorOptions* _optionsCell;

    [System.Runtime.InteropServices.UnmanagedCallersOnly(
        CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static unsafe byte QueryProgressThunk(TSQueryCursorState* state)
    {
        long* deadline = (long*)state->Payload;
        return System.Diagnostics.Stopwatch.GetTimestamp() >= *deadline ? (byte)1 : (byte)0;
    }

    /// <summary>
    /// Advances to the next match, copying its captures into managed memory.
    /// </summary>
    /// <param name="match">The next match, if any.</param>
    /// <returns><see langword="true"/> if a match was produced.</returns>
    /// <remarks>
    /// Must be called only after <see cref="Exec"/>. The <see cref="Query"/> passed to
    /// <see cref="Exec"/> and the owning <see cref="Tree"/> of the executed node must
    /// remain alive and undisposed until iteration completes; the native cursor
    /// dereferences their state on every call.
    /// </remarks>
    public unsafe bool NextMatch(out QueryMatch match)
    {
        TSQueryMatch raw;
        if (!NativeMethods.ts_query_cursor_next_match(Handle, &raw))
        {
            match = default;
            return false;
        }
        match = Convert(in raw);
        return true;
    }

    /// <summary>
    /// Advances to the next capture, copying the enclosing match's captures into
    /// managed memory and reporting which capture within it triggered the result.
    /// </summary>
    /// <param name="match">The match the capture belongs to.</param>
    /// <param name="captureIndex">The index of the triggering capture within <paramref name="match"/>.</param>
    /// <returns><see langword="true"/> if a capture was produced.</returns>
    public unsafe bool NextCapture(out QueryMatch match, out uint captureIndex)
    {
        TSQueryMatch raw;
        uint index;
        if (!NativeMethods.ts_query_cursor_next_capture(Handle, &raw, &index))
        {
            match = default;
            captureIndex = 0;
            return false;
        }
        match = Convert(in raw);
        captureIndex = index;
        return true;
    }

    /// <summary>
    /// Executes <paramref name="query"/> against <paramref name="node"/> and lazily
    /// yields every match.
    /// </summary>
    /// <param name="query">The query to run.</param>
    /// <param name="node">The node to run it against.</param>
    /// <remarks>
    /// The <paramref name="query"/> and the owning <see cref="Tree"/> of
    /// <paramref name="node"/> must remain alive and undisposed while the returned
    /// sequence is being enumerated; the native cursor dereferences their state on
    /// each step.
    /// </remarks>
    public IEnumerable<QueryMatch> Matches(Query query, Node node)
    {
        Exec(query, node);
        while (NextMatch(out QueryMatch match))
            yield return match;
    }

    /// <summary>
    /// Executes <paramref name="query"/> against <paramref name="node"/> and lazily
    /// yields every capture, paired with its index within the enclosing match.
    /// </summary>
    /// <param name="query">The query to run.</param>
    /// <param name="node">The node to run it against.</param>
    public IEnumerable<(QueryMatch match, uint index)> Captures(Query query, Node node)
    {
        Exec(query, node);
        while (NextCapture(out QueryMatch match, out uint index))
            yield return (match, index);
    }

    /// <summary>
    /// The maximum number of in-progress matches the cursor will retain. Exceeding
    /// it silently drops the earliest-starting match (see <see cref="DidExceedMatchLimit"/>).
    /// </summary>
    public uint MatchLimit
    {
        get => NativeMethods.ts_query_cursor_match_limit(Handle);
        set => NativeMethods.ts_query_cursor_set_match_limit(Handle, value);
    }

    /// <summary>Whether the configured <see cref="MatchLimit"/> was exceeded during execution.</summary>
    public bool DidExceedMatchLimit => NativeMethods.ts_query_cursor_did_exceed_match_limit(Handle);

    /// <summary>
    /// Restricts execution to matches intersecting the byte range
    /// <c>[startByte, endByte)</c>. An <paramref name="endByte"/> of <c>0</c> means unbounded.
    /// </summary>
    /// <param name="startByte">The start byte offset.</param>
    /// <param name="endByte">The end byte offset, or <c>0</c> for unbounded.</param>
    public void SetByteRange(uint startByte, uint endByte)
    {
        if (!NativeMethods.ts_query_cursor_set_byte_range(Handle, startByte, endByte))
            throw new ArgumentException("The start byte must not be greater than the end byte.");
    }

    /// <summary>
    /// Restricts execution to matches intersecting the point range. An
    /// <paramref name="endPoint"/> of <c>(0, 0)</c> means unbounded.
    /// </summary>
    /// <param name="startPoint">The start position.</param>
    /// <param name="endPoint">The end position, or <c>(0, 0)</c> for unbounded.</param>
    public void SetPointRange(Point startPoint, Point endPoint)
    {
        if (!NativeMethods.ts_query_cursor_set_point_range(Handle, startPoint.ToNative(), endPoint.ToNative()))
            throw new ArgumentException("The start point must not be greater than the end point.");
    }

    /// <summary>
    /// Restricts the depth at which pattern root nodes are searched. <c>0</c> is a
    /// special "stay on this node" mode; <see cref="uint.MaxValue"/> removes the limit.
    /// </summary>
    /// <param name="maxStartDepth">The maximum start depth.</param>
    public void SetMaxStartDepth(uint maxStartDepth) =>
        NativeMethods.ts_query_cursor_set_max_start_depth(Handle, maxStartDepth);

    /// <summary>
    /// Sets a best-effort timeout for query execution via the progress callback.
    /// </summary>
    /// <param name="timeoutMicros">The timeout in microseconds; <c>0</c> disables it.</param>
    /// <remarks>
    /// ABI 15 exposes no direct timeout setter on a query cursor, so when this is set
    /// to a non-zero value the next <see cref="Exec"/> routes through
    /// <c>ts_query_cursor_exec_with_options</c> with a progress callback that cancels
    /// execution once the deadline elapses.
    /// </remarks>
    public void SetTimeoutMicros(ulong timeoutMicros) => _timeoutMicros = timeoutMicros;

    private ulong _timeoutMicros;

    /// <summary>Converts a native match into a managed match, copying captures out.</summary>
    private unsafe QueryMatch Convert(in TSQueryMatch raw)
    {
        QueryCapture[] captures;
        if (raw.CaptureCount == 0 || raw.Captures == IntPtr.Zero)
        {
            captures = [];
        }
        else
        {
            captures = new QueryCapture[raw.CaptureCount];
            TSQueryCapture* arr = (TSQueryCapture*)raw.Captures;
            for (int i = 0; i < raw.CaptureCount; i++)
                captures[i] = new QueryCapture(new Node(arr[i].Node, _tree), arr[i].Index);
        }
        return new QueryMatch(raw.Id, raw.PatternIndex, captures);
    }

    /// <summary>
    /// Disposes the underlying native cursor (idempotent via
    /// <see cref="QueryCursorHandle"/>) and frees the auxiliary deadline cell.
    /// </summary>
    public void Dispose()
    {
        _handle.Dispose();
        FreeDeadlineCell();
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the unmanaged deadline cell allocated for timed execution, if any.</summary>
    private unsafe void FreeDeadlineCell()
    {
        if (_deadlineCell is not null)
        {
            System.Runtime.InteropServices.NativeMemory.Free(_deadlineCell);
            _deadlineCell = null;
        }
    }
}
