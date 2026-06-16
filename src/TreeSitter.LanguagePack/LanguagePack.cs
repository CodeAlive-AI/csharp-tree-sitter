using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using TreeSitter;

namespace TreeSitter.LanguagePack;

/// <summary>
/// Resolves tree-sitter grammars <em>by name</em> from the bundled
/// tree-sitter-language-pack manifest. Exposes the manifest metadata
/// (<see cref="GetInfo(string)"/>, <see cref="AvailableLanguages"/>,
/// extension lookup) and loads the corresponding native grammar library on demand,
/// wrapping its <c>const TSLanguage*</c> as a <see cref="Language"/>.
/// </summary>
/// <remarks>
/// <para>
/// A grammar is loadable only if its native library
/// (<c>libtree-sitter-&lt;c_symbol&gt;.so</c>/<c>.dylib</c>/<c>.dll</c>) has been built
/// and is discoverable (via <c>TREE_SITTER_NATIVE_PATH</c>, the NuGet
/// <c>runtimes/&lt;rid&gt;/native</c> layout, or next to this assembly). Use
/// <c>scripts/fetch-grammar.sh &lt;name&gt;</c> to build one.
/// </para>
/// <para>
/// Loaded <see cref="Language"/> instances are cached per name; the cache is
/// thread-safe and a given language is materialized at most once.
/// </para>
/// </remarks>
public static class LanguagePack
{
    // Parsed manifest (name -> info) and the extension index, built from a SINGLE
    // deserialization of the embedded JSON so the (large) manifest is parsed only once.
    private static readonly (FrozenDictionary<string, LanguageInfo> Manifest,
        FrozenDictionary<string, IReadOnlyList<string>> ByExtension) Loaded = Manifests.LoadAll();

    // Parsed manifest: name -> info. Frozen for fast, immutable lookups.
    private static FrozenDictionary<string, LanguageInfo> Manifest => Loaded.Manifest;

    // Sorted list of all manifest keys (stable, allocated once).
    private static readonly IReadOnlyList<string> SortedNames =
        Loaded.Manifest.Keys.OrderBy(static k => k, StringComparer.Ordinal).ToArray();

    // extension (no dot, lowercase) -> languages that claim it. Built from each
    // entry's `extensions` plus the alternatives listed under `ambiguous`.
    private static FrozenDictionary<string, IReadOnlyList<string>> ByExtension => Loaded.ByExtension;

    // Cache of materialized Language objects, keyed by language name.
    private static readonly ConcurrentDictionary<string, Language> LanguageCache =
        new(StringComparer.Ordinal);

    /// <summary>
    /// All language keys defined by the manifest, sorted ordinally. The count equals
    /// the number of grammars in tree-sitter-language-pack (currently 306).
    /// </summary>
    public static IReadOnlyCollection<string> AvailableLanguages => SortedNames;

    /// <summary>Returns <see langword="true"/> if <paramref name="name"/> is a known manifest language.</summary>
    /// <param name="name">The language key (case-sensitive, lowercase).</param>
    public static bool IsDefined(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Manifest.ContainsKey(name);
    }

    /// <summary>
    /// Gets the manifest metadata for <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The language key, e.g. <c>"python"</c>.</param>
    /// <returns>The <see cref="LanguageInfo"/> for the language.</returns>
    /// <exception cref="KeyNotFoundException"><paramref name="name"/> is not a defined language.</exception>
    public static LanguageInfo GetInfo(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Manifest.TryGetValue(name, out LanguageInfo? info))
            return info;
        throw new KeyNotFoundException(
            $"Unknown language '{name}'. It is not defined in the tree-sitter-language-pack manifest. " +
            $"Use {nameof(LanguagePack)}.{nameof(IsDefined)} to test, or {nameof(AvailableLanguages)} to enumerate the {SortedNames.Count} known languages.");
    }

    /// <summary>
    /// Attempts to get the manifest metadata for <paramref name="name"/> without throwing.
    /// </summary>
    /// <param name="name">The language key.</param>
    /// <param name="info">On success, the language metadata; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the language is defined.</returns>
    public static bool TryGetInfo(string name, [NotNullWhen(true)] out LanguageInfo? info)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Manifest.TryGetValue(name, out info);
    }

    /// <summary>
    /// Loads (and caches) the tree-sitter <see cref="Language"/> for <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The language key, e.g. <c>"python"</c>.</param>
    /// <returns>The cached <see cref="Language"/> for the grammar.</returns>
    /// <exception cref="KeyNotFoundException"><paramref name="name"/> is not a defined language.</exception>
    /// <exception cref="LanguageNotAvailableException">The native grammar library or its export symbol could not be loaded.</exception>
    public static Language Get(string name)
    {
        // Validate first so an unknown name yields KeyNotFoundException, not a cache miss.
        LanguageInfo info = GetInfo(name);
        return LanguageCache.GetOrAdd(info.Name, static (_, i) => Load(i), info);
    }

    /// <summary>
    /// Attempts to load the <see cref="Language"/> for <paramref name="name"/> without throwing.
    /// </summary>
    /// <param name="name">The language key.</param>
    /// <param name="language">On success, the loaded language; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the language is defined and its native grammar loaded.</returns>
    public static bool TryGet(string name, [NotNullWhen(true)] out Language? language)
    {
        ArgumentNullException.ThrowIfNull(name);
        language = null;
        if (!Manifest.TryGetValue(name, out LanguageInfo? info))
            return false;
        try
        {
            language = LanguageCache.GetOrAdd(info.Name, static (_, i) => Load(i), info);
            return true;
        }
        catch (LanguageNotAvailableException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a new <see cref="Parser"/> with its <see cref="Parser.Language"/> set to
    /// the grammar named <paramref name="name"/>. The caller owns and must dispose the parser.
    /// </summary>
    /// <param name="name">The language key, e.g. <c>"python"</c>.</param>
    /// <returns>A parser ready to <see cref="Parser.Parse(string, Tree?)"/> the language.</returns>
    /// <exception cref="KeyNotFoundException"><paramref name="name"/> is not a defined language.</exception>
    /// <exception cref="LanguageNotAvailableException">The native grammar library could not be loaded.</exception>
    public static Parser CreateParser(string name) => new(Get(name));

    /// <summary>
    /// Returns the single best language that claims file extension <paramref name="ext"/>,
    /// or <see langword="null"/> if none does.
    /// </summary>
    /// <param name="ext">An extension, with or without a leading dot (e.g. <c>".py"</c> or <c>"py"</c>); case-insensitive.</param>
    /// <remarks>
    /// When several languages share an extension, the primary owner (the language whose
    /// own <c>extensions</c> list contains it) is preferred; ties are broken by ordinal
    /// name order. Use <see cref="FindAllByExtension(string)"/> to see every candidate.
    /// </remarks>
    public static string? FindByExtension(string ext)
    {
        IReadOnlyList<string> all = FindAllByExtension(ext);
        return all.Count == 0 ? null : all[0];
    }

    /// <summary>
    /// Returns every language that claims file extension <paramref name="ext"/>, ordered
    /// with primary owners first then alphabetically; empty if none.
    /// </summary>
    /// <param name="ext">An extension, with or without a leading dot; case-insensitive.</param>
    public static IReadOnlyList<string> FindAllByExtension(string ext)
    {
        ArgumentNullException.ThrowIfNull(ext);
        string key = NormalizeExtension(ext);
        return key.Length != 0 && ByExtension.TryGetValue(key, out IReadOnlyList<string>? langs)
            ? langs
            : [];
    }

    // --- internals --------------------------------------------------------------

    /// <summary>Normalizes an extension to the manifest form: no leading dot, lowercase.</summary>
    internal static string NormalizeExtension(string ext)
    {
        string trimmed = ext.Trim();
        if (trimmed.StartsWith('.'))
            trimmed = trimmed[1..];
        return trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// Loads the native grammar library for <paramref name="info"/>, invokes its
    /// <c>tree_sitter_&lt;c_symbol&gt;</c> export, and wraps the result.
    /// </summary>
    private static unsafe Language Load(LanguageInfo info)
    {
        nint handle;
        try
        {
            // Resolve through the TreeSitter NativeLibraryResolver's probe sequence so
            // TREE_SITTER_NATIVE_PATH, the runtimes/<rid>/native layout, the assembly
            // directory, and the repo native/<rid>/ dev fallback ALL apply to grammar
            // loads (see docs/ARCHITECTURE.md and LanguageNotAvailableException).
            //
            // We must call the resolver explicitly: NativeLibrary.Load(name, assembly,
            // searchPath) does NOT invoke the DllImport resolver registered via
            // SetDllImportResolver — that callback only fires for DllImport/LibraryImport
            // P/Invoke. So delegate to the resolver's probe first, then fall back to the
            // default loader (which honours the OS search path and any embedded rpath).
            if (!global::TreeSitter.Native.NativeLibraryResolver.TryResolve(info.NativeLibraryName, out handle))
            {
                handle = System.Runtime.InteropServices.NativeLibrary.Load(
                    info.NativeLibraryName, typeof(global::TreeSitter.Language).Assembly, null);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or System.IO.FileNotFoundException or BadImageFormatException)
        {
            throw new LanguageNotAvailableException(info.Name, info.NativeLibraryName, ex);
        }

        if (!System.Runtime.InteropServices.NativeLibrary.TryGetExport(handle, info.ExportSymbol, out nint export))
            throw new LanguageNotAvailableException(info.Name, info.NativeLibraryName);

        var entry = (delegate* unmanaged[Cdecl]<nint>)export;
        nint languagePtr = entry();
        if (languagePtr == 0)
            throw new LanguageNotAvailableException(info.Name, info.NativeLibraryName);

        return new Language(languagePtr);
    }
}
