using System.Text;
using TreeSitter.Internal;
using TreeSitter.Native;

namespace TreeSitter;

/// <summary>
/// A compiled tree-sitter query: one or more S-expression patterns associated with
/// a <see cref="Language"/>. Owns native memory and must be disposed.
/// </summary>
public sealed class Query : IDisposable
{
    private IntPtr _handle;

    /// <summary>
    /// Compiles <paramref name="source"/> against <paramref name="language"/>.
    /// </summary>
    /// <param name="language">The language the query targets.</param>
    /// <param name="source">The query source (one or more S-expression patterns).</param>
    /// <exception cref="QueryException">The query source is invalid.</exception>
    public unsafe Query(Language language, string source)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(source);

        byte[] bytes = Encoding.UTF8.GetBytes(source);
        uint errorOffset;
        int errorType;
        IntPtr handle;
        fixed (byte* p = bytes)
            handle = NativeMethods.ts_query_new(language.Handle, p, (uint)bytes.Length, &errorOffset, &errorType);

        if (handle == IntPtr.Zero)
            throw new QueryException(errorOffset, (QueryError)errorType);

        _handle = handle;
    }

    /// <summary>Frees the native query if it was not explicitly disposed.</summary>
    ~Query() => DisposeCore();

    internal IntPtr Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
            return _handle;
        }
    }

    /// <summary>The number of patterns in the query.</summary>
    public uint PatternCount => NativeMethods.ts_query_pattern_count(Handle);

    /// <summary>The number of distinct captures in the query.</summary>
    public uint CaptureCount => NativeMethods.ts_query_capture_count(Handle);

    /// <summary>The number of distinct string literals in the query.</summary>
    public uint StringCount => NativeMethods.ts_query_string_count(Handle);

    /// <summary>The byte offset at which a pattern starts in the query source.</summary>
    /// <param name="patternIndex">The pattern index.</param>
    public uint StartByteForPattern(uint patternIndex) =>
        NativeMethods.ts_query_start_byte_for_pattern(Handle, patternIndex);

    /// <summary>The byte offset at which a pattern ends in the query source.</summary>
    /// <param name="patternIndex">The pattern index.</param>
    public uint EndByteForPattern(uint patternIndex) =>
        NativeMethods.ts_query_end_byte_for_pattern(Handle, patternIndex);

    /// <summary>The name of the capture with the given id.</summary>
    /// <param name="id">The capture id.</param>
    public unsafe string CaptureNameForId(uint id)
    {
        uint length;
        IntPtr ptr = NativeMethods.ts_query_capture_name_for_id(Handle, id, &length);
        return Utf8.PtrToString(ptr, length) ?? string.Empty;
    }

    /// <summary>The value of the string literal with the given id.</summary>
    /// <param name="id">The string id.</param>
    public unsafe string StringValueForId(uint id)
    {
        uint length;
        IntPtr ptr = NativeMethods.ts_query_string_value_for_id(Handle, id, &length);
        return Utf8.PtrToString(ptr, length) ?? string.Empty;
    }

    /// <summary>The quantifier of a capture within a pattern.</summary>
    /// <param name="patternIndex">The pattern index.</param>
    /// <param name="captureIndex">The capture id.</param>
    public Quantifier CaptureQuantifierForId(uint patternIndex, uint captureIndex) =>
        (Quantifier)NativeMethods.ts_query_capture_quantifier_for_id(Handle, patternIndex, captureIndex);

    /// <summary>Whether the pattern has a single root node.</summary>
    /// <param name="patternIndex">The pattern index.</param>
    public bool IsPatternRooted(uint patternIndex) =>
        NativeMethods.ts_query_is_pattern_rooted(Handle, patternIndex);

    /// <summary>Whether the pattern is non-local (matches within a repeating sequence).</summary>
    /// <param name="patternIndex">The pattern index.</param>
    public bool IsPatternNonLocal(uint patternIndex) =>
        NativeMethods.ts_query_is_pattern_non_local(Handle, patternIndex);

    /// <summary>Whether a match is guaranteed once the given query-source byte offset is reached.</summary>
    /// <param name="byteOffset">A byte offset within the query source.</param>
    public bool IsPatternGuaranteedAtStep(uint byteOffset) =>
        NativeMethods.ts_query_is_pattern_guaranteed_at_step(Handle, byteOffset);

    /// <summary>
    /// Permanently disables a capture by name, preventing it from appearing in
    /// matches. This cannot be undone.
    /// </summary>
    /// <param name="name">The capture name to disable.</param>
    public unsafe void DisableCapture(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        byte[] bytes = Encoding.UTF8.GetBytes(name);
        fixed (byte* p = bytes)
            NativeMethods.ts_query_disable_capture(Handle, p, (uint)bytes.Length);
    }

    /// <summary>Permanently disables a pattern by index. This cannot be undone.</summary>
    /// <param name="patternIndex">The pattern index to disable.</param>
    public void DisablePattern(uint patternIndex) =>
        NativeMethods.ts_query_disable_pattern(Handle, patternIndex);

    /// <summary>
    /// Gets the predicate steps for a pattern. The returned list ends with one or
    /// more <see cref="QueryPredicateStepType.Done"/> steps, each marking the end of
    /// an individual predicate.
    /// </summary>
    /// <param name="patternIndex">The pattern index.</param>
    public unsafe IReadOnlyList<QueryPredicateStep> PredicatesForPattern(uint patternIndex)
    {
        uint stepCount;
        TSQueryPredicateStep* ptr = NativeMethods.ts_query_predicates_for_pattern(Handle, patternIndex, &stepCount);
        if (ptr is null || stepCount == 0)
            return [];

        var result = new QueryPredicateStep[stepCount];
        for (uint i = 0; i < stepCount; i++)
            result[i] = new QueryPredicateStep((QueryPredicateStepType)ptr[i].Type, ptr[i].ValueId);
        return result;
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
            NativeMethods.ts_query_delete(handle);
    }
}
