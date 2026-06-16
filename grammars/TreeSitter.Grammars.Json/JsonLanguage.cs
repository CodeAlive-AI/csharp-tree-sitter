using TreeSitter;

// Alias the static facade to avoid the namespace/type name clash: the type
// 'TreeSitter.LanguagePack.LanguagePack' lives in a namespace of the same name, so a
// bare 'LanguagePack.Get(...)' would bind to the namespace. The alias is unambiguous.
using Pack = global::TreeSitter.LanguagePack.LanguagePack;

namespace TreeSitter.Grammars.Json;

/// <summary>
/// Provides the tree-sitter <see cref="TreeSitter.Language"/> for the JSON grammar
/// and a convenience <see cref="Parser"/> factory. The grammar is resolved by name
/// through <see cref="LanguagePack"/>, which loads and caches
/// <c>libtree-sitter-json</c> on demand.
/// </summary>
public static class JsonLanguage
{
    /// <summary>The canonical language name in the tree-sitter-language-pack manifest.</summary>
    public const string Name = "json";

    /// <summary>
    /// The JSON <see cref="TreeSitter.Language"/>. The underlying <c>TSLanguage*</c>
    /// is statically allocated by the grammar and lives for the process lifetime;
    /// <see cref="LanguagePack"/> caches the wrapper, so this returns the same instance
    /// on every call.
    /// </summary>
    /// <exception cref="TreeSitter.LanguagePack.LanguageNotAvailableException">
    /// The native <c>libtree-sitter-json</c> library could not be loaded (build it
    /// with <c>scripts/fetch-grammar.sh json</c>).
    /// </exception>
    public static Language Language => Pack.Get(Name);

    /// <summary>
    /// Creates a new <see cref="Parser"/> bound to the JSON grammar. The caller owns
    /// and must dispose the returned parser.
    /// </summary>
    public static Parser CreateParser() => Pack.CreateParser(Name);
}
