using TreeSitter.Internal;
using TreeSitter.Native;

namespace TreeSitter;

/// <summary>
/// Iterates the symbols that are valid in a given parse state, useful for building
/// completion suggestions and error diagnostics. Owns native memory and must be
/// disposed. A freshly created or reset iterator is positioned at the <c>ERROR</c>
/// symbol; call <see cref="Next"/> to begin iterating.
/// </summary>
public sealed class LookaheadIterator : IDisposable
{
    private IntPtr _handle;
    private Language _language;

    private LookaheadIterator(IntPtr handle, Language language)
    {
        _handle = handle;
        _language = language;
    }

    /// <summary>
    /// Creates a lookahead iterator for <paramref name="language"/> at
    /// <paramref name="state"/>.
    /// </summary>
    /// <param name="language">The language whose states to inspect.</param>
    /// <param name="state">The parse state to begin at.</param>
    /// <exception cref="ArgumentException"><paramref name="state"/> is invalid for the language.</exception>
    public LookaheadIterator(Language language, ushort state)
    {
        ArgumentNullException.ThrowIfNull(language);
        IntPtr handle = NativeMethods.ts_lookahead_iterator_new(language.Handle, state);
        if (handle == IntPtr.Zero)
            throw new ArgumentException($"State {state} is invalid for the given language.", nameof(state));
        _handle = handle;
        _language = language;
    }

    /// <summary>
    /// Attempts to create a lookahead iterator, returning <see langword="null"/> if
    /// the state is invalid for the language.
    /// </summary>
    internal static LookaheadIterator? TryCreate(Language language, ushort state)
    {
        IntPtr handle = NativeMethods.ts_lookahead_iterator_new(language.Handle, state);
        return handle == IntPtr.Zero ? null : new LookaheadIterator(handle, language);
    }

    /// <summary>Frees the native iterator if it was not explicitly disposed.</summary>
    ~LookaheadIterator() => DisposeCore();

    private IntPtr Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
            return _handle;
        }
    }

    /// <summary>The language the iterator is currently associated with.</summary>
    public Language Language => _language;

    /// <summary>
    /// Resets the iterator to a new <paramref name="language"/> and
    /// <paramref name="state"/>. Returns whether the reset succeeded.
    /// </summary>
    /// <param name="language">The new language.</param>
    /// <param name="state">The new parse state.</param>
    public bool Reset(Language language, ushort state)
    {
        ArgumentNullException.ThrowIfNull(language);
        bool ok = NativeMethods.ts_lookahead_iterator_reset(Handle, language.Handle, state);
        if (ok)
            _language = language;
        return ok;
    }

    /// <summary>Resets the iterator to a new <paramref name="state"/> in the same language.</summary>
    /// <param name="state">The new parse state.</param>
    /// <returns>Whether the reset succeeded.</returns>
    public bool ResetState(ushort state) => NativeMethods.ts_lookahead_iterator_reset_state(Handle, state);

    /// <summary>Advances to the next valid symbol. Returns whether a new symbol is available.</summary>
    public bool Next() => NativeMethods.ts_lookahead_iterator_next(Handle);

    /// <summary>The current symbol id.</summary>
    public ushort CurrentSymbol => NativeMethods.ts_lookahead_iterator_current_symbol(Handle);

    /// <summary>The current symbol's name, or <see langword="null"/> if unavailable.</summary>
    public string? CurrentSymbolName =>
        Utf8.PtrToString(NativeMethods.ts_lookahead_iterator_current_symbol_name(Handle));

    /// <summary>
    /// Lazily enumerates the valid symbol ids from the current position to the end.
    /// </summary>
    public IEnumerable<ushort> Symbols()
    {
        while (Next())
            yield return CurrentSymbol;
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
            NativeMethods.ts_lookahead_iterator_delete(handle);
    }
}
