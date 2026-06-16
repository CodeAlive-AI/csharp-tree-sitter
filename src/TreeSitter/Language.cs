using System.Runtime.InteropServices;
using TreeSitter.Internal;
using TreeSitter.Native;

namespace TreeSitter;

/// <summary>
/// A tree-sitter language: the compiled grammar used to parse a particular
/// programming language. Wraps a <c>const TSLanguage*</c>.
/// </summary>
/// <remarks>
/// Language objects obtained from a grammar's <c>tree_sitter_&lt;name&gt;()</c>
/// entry point are statically allocated and live for the lifetime of the process;
/// they require no disposal. A <see cref="Copy"/> takes an additional reference
/// (relevant for Wasm languages); the binding tracks ownership internally and the
/// extra reference is released by the finalizer when the copy becomes unreachable.
/// </remarks>
public sealed class Language
{
    private readonly IntPtr _handle;
    private readonly bool _owned;

    // Lazily-populated caches of symbol and field names, indexed by id.
    private string?[]? _symbolNames;
    private string?[]? _fieldNames;
    private readonly Lock _cacheLock = new();

    /// <summary>
    /// Wraps an existing <c>const TSLanguage*</c>. Use this with the pointer
    /// returned by a grammar's <c>tree_sitter_&lt;name&gt;()</c> function.
    /// </summary>
    /// <param name="handle">A non-null pointer to a <c>TSLanguage</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="handle"/> is null.</exception>
    public Language(IntPtr handle) : this(handle, owned: false) { }

    private Language(IntPtr handle, bool owned)
    {
        if (handle == IntPtr.Zero)
            throw new ArgumentException("The language pointer must not be null.", nameof(handle));
        _handle = handle;
        _owned = owned;
    }

    /// <summary>Frees the language's extra reference if this instance owns one.</summary>
    ~Language()
    {
        if (_owned && _handle != IntPtr.Zero)
            NativeMethods.ts_language_delete(_handle);
    }

    /// <summary>The raw <c>const TSLanguage*</c> handle backing this language.</summary>
    internal IntPtr Handle => _handle;

    /// <summary>The language's ABI version (e.g. 15 for ABI 15).</summary>
    public uint AbiVersion => NativeMethods.ts_language_abi_version(_handle);

    /// <summary>
    /// The language's name (e.g. <c>"json"</c>, <c>"python"</c>), or
    /// <see langword="null"/> for parsers generated before ABI 15.
    /// </summary>
    public string? Name => Utf8.PtrToString(NativeMethods.ts_language_name(_handle));

    /// <summary>The number of distinct node-type symbols in the language.</summary>
    public uint SymbolCount => NativeMethods.ts_language_symbol_count(_handle);

    /// <summary>The number of distinct parse states in the language.</summary>
    public uint StateCount => NativeMethods.ts_language_state_count(_handle);

    /// <summary>The number of distinct field names in the language.</summary>
    public uint FieldCount => NativeMethods.ts_language_field_count(_handle);

    /// <summary>The language's semantic version metadata, if present (ABI 15+).</summary>
    public unsafe Version? Metadata
    {
        get
        {
            TSLanguageMetadata* meta = NativeMethods.ts_language_metadata(_handle);
            return meta is null
                ? null
                : new Version(meta->MajorVersion, meta->MinorVersion, meta->PatchVersion);
        }
    }

    /// <summary>
    /// Gets the node-type string for a symbol id, or <see langword="null"/> if the
    /// id is out of range. Results are cached.
    /// </summary>
    /// <param name="symbol">The symbol id.</param>
    public string? SymbolName(ushort symbol)
    {
        string?[] cache = EnsureSymbolNames();
        return symbol < cache.Length ? cache[symbol] : null;
    }

    /// <summary>
    /// Gets the symbol id for a node-type name, or <c>0</c> (the end sentinel) if
    /// the name is unknown.
    /// </summary>
    /// <param name="name">The node-type name.</param>
    /// <param name="isNamed">Whether to look up a named rule (vs. an anonymous token).</param>
    public unsafe ushort SymbolForName(string name, bool isNamed)
    {
        ArgumentNullException.ThrowIfNull(name);
        int byteCount = Utf8.ByteCount(name);
        Span<byte> buffer = byteCount <= 256 ? stackalloc byte[byteCount] : new byte[byteCount];
        System.Text.Encoding.UTF8.GetBytes(name, buffer);
        fixed (byte* p = buffer)
            return NativeMethods.ts_language_symbol_for_name(_handle, p, (uint)byteCount, isNamed);
    }

    /// <summary>Gets the category (named / anonymous / supertype / hidden) of a symbol id.</summary>
    /// <param name="symbol">The symbol id.</param>
    public SymbolType SymbolType(ushort symbol) =>
        (SymbolType)NativeMethods.ts_language_symbol_type(_handle, symbol);

    /// <summary>
    /// Gets the field name for a field id, or <see langword="null"/> if the id is
    /// invalid. Results are cached.
    /// </summary>
    /// <param name="fieldId">The field id.</param>
    public string? FieldNameForId(ushort fieldId)
    {
        string?[] cache = EnsureFieldNames();
        return fieldId < cache.Length ? cache[fieldId] : null;
    }

    /// <summary>
    /// Gets the field id for a field name, or <c>0</c> if the name is unknown.
    /// </summary>
    /// <param name="name">The field name.</param>
    public unsafe ushort FieldIdForName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        int byteCount = Utf8.ByteCount(name);
        Span<byte> buffer = byteCount <= 256 ? stackalloc byte[byteCount] : new byte[byteCount];
        System.Text.Encoding.UTF8.GetBytes(name, buffer);
        fixed (byte* p = buffer)
            return NativeMethods.ts_language_field_id_for_name(_handle, p, (uint)byteCount);
    }

    /// <summary>
    /// Gets the parse state reached after observing <paramref name="symbol"/> in
    /// <paramref name="state"/>. Use a node's <see cref="Node.GrammarSymbol"/> (not
    /// <see cref="Node.KindId"/>) for correctness.
    /// </summary>
    /// <param name="state">The current parse state.</param>
    /// <param name="symbol">The grammar symbol observed.</param>
    public ushort NextState(ushort state, ushort symbol) =>
        NativeMethods.ts_language_next_state(_handle, state, symbol);

    /// <summary>Gets the list of all supertype symbol ids defined by the language.</summary>
    public unsafe IReadOnlyList<ushort> Supertypes()
    {
        uint length;
        ushort* ptr = NativeMethods.ts_language_supertypes(_handle, &length);
        return CopySymbolArray(ptr, length);
    }

    /// <summary>Gets the list of concrete subtype symbol ids for a supertype symbol.</summary>
    /// <param name="supertype">The supertype symbol id.</param>
    public unsafe IReadOnlyList<ushort> Subtypes(ushort supertype)
    {
        uint length;
        ushort* ptr = NativeMethods.ts_language_subtypes(_handle, supertype, &length);
        return CopySymbolArray(ptr, length);
    }

    /// <summary>
    /// Returns a new <see cref="Language"/> that holds an additional reference to
    /// the same underlying grammar. For statically-linked grammars this is
    /// effectively free and the extra reference is a no-op on release.
    /// </summary>
    public Language Copy()
    {
        IntPtr copy = NativeMethods.ts_language_copy(_handle);
        return new Language(copy, owned: true);
    }

    private static unsafe ushort[] CopySymbolArray(ushort* ptr, uint length)
    {
        if (ptr is null || length == 0)
            return [];
        var result = new ushort[length];
        new ReadOnlySpan<ushort>(ptr, checked((int)length)).CopyTo(result);
        return result;
    }

    private string?[] EnsureSymbolNames()
    {
        // Volatile read so a consumer on a weak-memory-model CPU (e.g. osx-arm64) sees a
        // fully-initialized array, never a partially-constructed one published by another
        // thread. The write below is paired with Volatile.Write.
        string?[]? cache = Volatile.Read(ref _symbolNames);
        if (cache is not null)
            return cache;

        lock (_cacheLock)
        {
            cache = Volatile.Read(ref _symbolNames);
            if (cache is not null)
                return cache;

            uint count = SymbolCount;
            var names = new string?[count];
            for (ushort i = 0; i < count; i++)
                names[i] = Utf8.PtrToString(NativeMethods.ts_language_symbol_name(_handle, i));
            Volatile.Write(ref _symbolNames, names);
            return names;
        }
    }

    private string?[] EnsureFieldNames()
    {
        string?[]? cache = Volatile.Read(ref _fieldNames);
        if (cache is not null)
            return cache;

        lock (_cacheLock)
        {
            cache = Volatile.Read(ref _fieldNames);
            if (cache is not null)
                return cache;

            // Field ids are 1-based; index 0 is always null. Allocate count + 1.
            uint count = FieldCount;
            var names = new string?[count + 1];
            for (ushort i = 0; i <= count; i++)
                names[i] = Utf8.PtrToString(NativeMethods.ts_language_field_name_for_id(_handle, i));
            Volatile.Write(ref _fieldNames, names);
            return names;
        }
    }

    /// <summary>
    /// Creates a <see cref="LookaheadIterator"/> over the valid symbols at a parse
    /// state, or <see langword="null"/> if the state is invalid for this language.
    /// </summary>
    /// <param name="state">The parse state to inspect.</param>
    public LookaheadIterator? LookaheadIterator(ushort state) =>
        TreeSitter.LookaheadIterator.TryCreate(this, state);
}
