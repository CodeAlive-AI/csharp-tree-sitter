using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using TreeSitter.Native;

namespace TreeSitter;

/// <summary>
/// Parses source code into a syntax <see cref="Tree"/> for a given
/// <see cref="Language"/>. A parser owns native memory and must be disposed.
/// Parsers are not thread-safe.
/// </summary>
public sealed class Parser : IDisposable
{
    private readonly ParserHandle _handle;
    private Language? _language;

    // Logger state. The managed delegate is rooted in this field, and a GCHandle to
    // `this` is stored as the native logger payload so the static unmanaged thunk can
    // dispatch back to the instance.
    private Action<LogType, string>? _logger;
    private GCHandle _selfHandle;

    // Managed timeout (ABI 15 removed the native timeout API; we enforce a deadline
    // via the parse progress callback instead). 0 means no timeout.
    private ulong _timeoutMicros;

    /// <summary>Creates a parser with no language assigned.</summary>
    public Parser()
    {
        _handle = ParserHandle.Create();
    }

    /// <summary>Creates a parser and assigns <paramref name="language"/>.</summary>
    /// <param name="language">The language to parse with.</param>
    /// <exception cref="LanguageVersionException">The language's ABI is incompatible.</exception>
    public Parser(Language language) : this()
    {
        Language = language;
    }

    private ParserHandle Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_handle.IsClosed || _handle.IsInvalid, this);
            return _handle;
        }
    }

    /// <summary>
    /// The language the parser uses. Setting an incompatible language throws a
    /// <see cref="LanguageVersionException"/>; use <see cref="TrySetLanguage"/> for
    /// a non-throwing alternative. Setting <see langword="null"/> clears the language.
    /// </summary>
    public Language? Language
    {
        get => _language;
        set
        {
            if (value is null)
            {
                NativeMethods.ts_parser_set_language(Handle, IntPtr.Zero);
                _language = null;
                return;
            }

            if (!NativeMethods.ts_parser_set_language(Handle, value.Handle))
                throw new LanguageVersionException(value.AbiVersion);
            _language = value;
        }
    }

    /// <summary>
    /// Attempts to assign <paramref name="language"/>, returning <see langword="false"/>
    /// instead of throwing if the language's ABI version is incompatible.
    /// </summary>
    /// <param name="language">The language to assign.</param>
    public bool TrySetLanguage(Language language)
    {
        ArgumentNullException.ThrowIfNull(language);
        if (!NativeMethods.ts_parser_set_language(Handle, language.Handle))
            return false;
        _language = language;
        return true;
    }

    /// <summary>
    /// The disjoint source ranges the parser will include when parsing. An empty
    /// array (the default) means the whole document is parsed.
    /// </summary>
    /// <exception cref="ArgumentException">The ranges are out of order or overlap.</exception>
    public unsafe Range[] IncludedRanges
    {
        get
        {
            uint count;
            TSRange* ptr = NativeMethods.ts_parser_included_ranges(Handle, &count);
            if (ptr is null || count == 0)
                return [];
            var result = new Range[count];
            for (uint i = 0; i < count; i++)
                result[i] = new Range(ptr[i]);
            return result;
        }
        set
        {
            value ??= [];
            Span<TSRange> native = value.Length <= 16
                ? stackalloc TSRange[value.Length]
                : new TSRange[value.Length];
            for (int i = 0; i < value.Length; i++)
                native[i] = value[i].ToNative();

            fixed (TSRange* p = native)
            {
                if (!NativeMethods.ts_parser_set_included_ranges(Handle, p, (uint)value.Length))
                    throw new ArgumentException(
                        "Included ranges must be ordered and non-overlapping.", nameof(value));
            }
        }
    }

    /// <summary>
    /// A best-effort maximum parse duration in microseconds. <c>0</c> (the default)
    /// disables the limit. When set, parsing that exceeds the limit is cancelled and
    /// <see cref="Parse(ReadOnlySpan{byte}, Tree?)"/> returns <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// ABI 15 removed the native timeout API, so this is enforced in managed code via
    /// the parse progress callback.
    /// </remarks>
    public ulong TimeoutMicros
    {
        get => _timeoutMicros;
        set => _timeoutMicros = value;
    }

    /// <summary>
    /// A callback that receives parser log messages, or <see langword="null"/> to
    /// disable logging. The delegate is rooted for the lifetime of the assignment.
    /// </summary>
    public unsafe Action<LogType, string>? Logger
    {
        get => _logger;
        set
        {
            _logger = value;
            if (value is null)
            {
                // Clear the native logger and release the self handle.
                NativeMethods.ts_parser_set_logger(Handle, default);
                if (_selfHandle.IsAllocated)
                    _selfHandle.Free();
                return;
            }

            if (!_selfHandle.IsAllocated)
                _selfHandle = GCHandle.Alloc(this, GCHandleType.Weak);

            var logger = new TSLogger
            {
                Payload = GCHandle.ToIntPtr(_selfHandle),
                Log = &LogThunk,
            };
            NativeMethods.ts_parser_set_logger(Handle, logger);
        }
    }

    /// <summary>
    /// Resets the parser so the next parse starts from scratch (discarding any
    /// incremental state retained after a cancelled parse).
    /// </summary>
    public void Reset() => NativeMethods.ts_parser_reset(Handle);

    /// <summary>
    /// Parses UTF-8 <paramref name="utf8Source"/>, optionally reusing
    /// <paramref name="oldTree"/> for incremental parsing.
    /// </summary>
    /// <param name="utf8Source">The UTF-8 encoded source to parse.</param>
    /// <param name="oldTree">A previously-parsed, edited tree to reuse, or <see langword="null"/>.</param>
    /// <returns>The parsed tree, or <see langword="null"/> if parsing was cancelled (timeout).</returns>
    /// <exception cref="InvalidOperationException">No language has been set on the parser.</exception>
    public Tree? Parse(ReadOnlySpan<byte> utf8Source, Tree? oldTree = null)
    {
        EnsureLanguage();

        // The tree must retain its own copy of the bytes so Node.Text stays valid.
        byte[] copy = utf8Source.ToArray();
        return ParseBytes(copy, oldTree);
    }

    /// <summary>
    /// Parses <paramref name="source"/> after encoding it as UTF-8, optionally
    /// reusing <paramref name="oldTree"/> for incremental parsing.
    /// </summary>
    /// <param name="source">The source text to parse.</param>
    /// <param name="oldTree">A previously-parsed, edited tree to reuse, or <see langword="null"/>.</param>
    /// <returns>The parsed tree, or <see langword="null"/> if parsing was cancelled (timeout).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No language has been set on the parser.</exception>
    public Tree? Parse(string source, Tree? oldTree = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureLanguage();
        byte[] bytes = Encoding.UTF8.GetBytes(source);
        return ParseBytes(bytes, oldTree);
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if no language has been set. A
    /// missing language is a programmer error (distinct from a <see langword="null"/>
    /// return, which is reserved for a cancelled/timed-out parse).
    /// </summary>
    private void EnsureLanguage()
    {
        if (_language is null)
            throw new InvalidOperationException(
                "No language has been set on the parser; set the Language (or use the " +
                "Language ctor) before parsing.");
    }

    private unsafe Tree? ParseBytes(byte[] utf8, Tree? oldTree)
    {
        // Capture the language non-null: callers (Parse overloads) guarantee it via
        // EnsureLanguage(), so we hold a local rather than dereferencing _language! later.
        Language language = _language
            ?? throw new InvalidOperationException("No language has been set on the parser.");

        // old_tree is a BORROWED pointer. When present, pin its SafeHandle open with
        // DangerousAddRef so the raw pointer cannot be freed by another thread while
        // the native parse reads it, releasing the ref once the call returns.
        TreeHandle? oldHandleSafe = oldTree?.Handle;
        bool oldRefAdded = false;
        if (oldHandleSafe is not null)
            oldHandleSafe.DangerousAddRef(ref oldRefAdded);
        try
        {
            IntPtr oldHandle = oldRefAdded ? oldHandleSafe!.DangerousGetHandle() : IntPtr.Zero;
            TreeHandle resultHandle;

            fixed (byte* p = utf8)
            {
                // An empty buffer needs a valid (non-null) pointer; tree-sitter reads
                // `length` bytes regardless. Use a scratch pointer for the zero case.
                byte scratch = 0;
                byte* src = utf8.Length == 0 ? &scratch : p;

                if (_timeoutMicros == 0)
                {
                    resultHandle = NativeMethods.ts_parser_parse_string_encoding(
                        Handle, oldHandle, src, (uint)utf8.Length, (int)InputEncoding.Utf8);
                }
                else
                {
                    resultHandle = ParseWithDeadline(oldHandle, src, (uint)utf8.Length);
                }
            }

            if (resultHandle.IsInvalid)
            {
                resultHandle.Dispose();
                return null;
            }

            return new Tree(resultHandle, utf8, language);
        }
        finally
        {
            if (oldRefAdded)
                oldHandleSafe!.DangerousRelease();
        }
    }

    /// <summary>
    /// Parses using <c>ts_parser_parse_with_options</c> with a progress callback
    /// that cancels the parse once the configured timeout elapses. A custom
    /// <see cref="TSInput"/> feeds the in-memory UTF-8 buffer in a single chunk.
    /// </summary>
    private unsafe TreeHandle ParseWithDeadline(IntPtr oldHandle, byte* src, uint length)
    {
        long deadlineTicks = Stopwatch.GetTimestamp() +
            (long)((double)_timeoutMicros / 1_000_000.0 * Stopwatch.Frequency);

        var state = new ParsePayload { Source = src, Length = length, DeadlineTicks = deadlineTicks };

        var input = new TSInput
        {
            Payload = (IntPtr)(&state),
            Read = &ReadThunk,
            Encoding = (int)InputEncoding.Utf8,
            Decode = null,
        };

        var options = new TSParseOptions
        {
            Payload = (IntPtr)(&state),
            ProgressCallback = &ProgressThunk,
        };

        return NativeMethods.ts_parser_parse_with_options(Handle, oldHandle, input, options);
    }

    // Payload shared between the read and progress thunks for a single parse.
    private unsafe struct ParsePayload
    {
        public byte* Source;
        public uint Length;
        public long DeadlineTicks;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static unsafe IntPtr ReadThunk(IntPtr payload, uint byteIndex, TSPoint position, uint* bytesRead)
    {
        ParsePayload* state = (ParsePayload*)payload;
        if (byteIndex >= state->Length)
        {
            *bytesRead = 0;
            return IntPtr.Zero;
        }
        *bytesRead = state->Length - byteIndex;
        return (IntPtr)(state->Source + byteIndex);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static unsafe byte ProgressThunk(TSParseState* parseState)
    {
        ParsePayload* state = (ParsePayload*)parseState->Payload;
        // Return true (1) to cancel once the deadline has passed.
        return Stopwatch.GetTimestamp() >= state->DeadlineTicks ? (byte)1 : (byte)0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static unsafe void LogThunk(IntPtr payload, int logType, IntPtr message)
    {
        if (payload == IntPtr.Zero)
            return;
        var handle = GCHandle.FromIntPtr(payload);
        if (handle.Target is not Parser parser)
            return;
        Action<LogType, string>? logger = parser._logger;
        if (logger is null)
            return;
        string text = Marshal.PtrToStringUTF8(message) ?? string.Empty;
        try
        {
            logger((LogType)logType, text);
        }
        catch
        {
            // Never let a managed exception propagate across the native boundary.
        }
    }

    /// <summary>
    /// Frees the logger <see cref="GCHandle"/> if the parser was not disposed. The
    /// native parser itself is reclaimed by <see cref="ParserHandle"/>'s critical
    /// finalizer, so this only releases the managed rooting handle.
    /// </summary>
    ~Parser()
    {
        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Detach the logger while the SafeHandle is still valid so the native side
        // stops referencing our payload before the parser is released, then dispose
        // the handle (idempotent) and free the rooting GCHandle.
        if (!_handle.IsClosed && !_handle.IsInvalid)
            NativeMethods.ts_parser_set_logger(_handle, default);
        _handle.Dispose();
        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
        GC.SuppressFinalize(this);
    }
}
