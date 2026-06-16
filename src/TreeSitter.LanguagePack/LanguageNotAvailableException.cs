namespace TreeSitter.LanguagePack;

/// <summary>
/// Thrown by <see cref="LanguagePack.Get(string)"/> when a language is defined in the
/// manifest but its native grammar library (or the expected export symbol) cannot be
/// loaded on this machine — typically because the grammar has not been built yet.
/// </summary>
/// <remarks>
/// The <see cref="Exception.Message"/> explains how to build the grammar (via
/// <c>build/build-native.sh</c> or <c>scripts/fetch-grammar.sh &lt;name&gt;</c>).
/// </remarks>
public sealed class LanguageNotAvailableException : TreeSitterException
{
    /// <summary>The language key that could not be loaded.</summary>
    public string LanguageName { get; }

    /// <summary>The logical native library that was expected (e.g. <c>tree-sitter-python</c>).</summary>
    public string NativeLibraryName { get; }

    /// <summary>Creates a new <see cref="LanguageNotAvailableException"/>.</summary>
    /// <param name="languageName">The language key that could not be loaded.</param>
    /// <param name="nativeLibraryName">The logical native library that was expected.</param>
    /// <param name="innerException">The underlying load failure, if any.</param>
    public LanguageNotAvailableException(
        string languageName,
        string nativeLibraryName,
        Exception? innerException = null)
        : base(BuildMessage(languageName, nativeLibraryName), innerException)
    {
        LanguageName = languageName;
        NativeLibraryName = nativeLibraryName;
    }

    private static string BuildMessage(string languageName, string nativeLibraryName) =>
        $"The grammar for '{languageName}' is not available: the native library " +
        $"'{nativeLibraryName}' (or its export symbol) could not be loaded. " +
        $"Build it first with 'scripts/fetch-grammar.sh {languageName}' (which clones the " +
        $"pinned source and runs build/build-native.sh), then ensure native/<rid>/ is on the " +
        $"native search path (set TREE_SITTER_NATIVE_PATH or ship runtimes/<rid>/native/).";
}
