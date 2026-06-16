namespace TreeSitter.LanguagePack;

/// <summary>
/// Immutable metadata describing one grammar in the bundled
/// tree-sitter-language-pack manifest. Mirrors a single entry of
/// <c>language_definitions.json</c>.
/// </summary>
/// <param name="Name">The canonical language key (lowercase, underscore-separated), e.g. <c>"python"</c>, <c>"csharp"</c>.</param>
/// <param name="Repo">The HTTPS URL of the upstream tree-sitter grammar repository.</param>
/// <param name="Rev">The pinned git commit SHA (or branch tip when no rev is recorded; empty string if neither).</param>
/// <param name="Branch">The branch to clone from, if specified; otherwise <see langword="null"/>.</param>
/// <param name="Directory">The sub-directory within the repository that holds the grammar (e.g. <c>"typescript"</c>), or <see langword="null"/>.</param>
/// <param name="Generate">Whether the grammar must be regenerated with <c>tree-sitter generate</c> before it can be compiled (it ships <c>grammar.js</c> but no pre-generated <c>parser.c</c>).</param>
/// <param name="CSymbol">The C export-symbol stem; defaults to <see cref="Name"/> unless the manifest overrides it (e.g. <c>"csharp"</c> exports <c>tree_sitter_c_sharp</c>).</param>
/// <param name="Extensions">The file extensions (no leading dot, lowercase) this language claims.</param>
/// <param name="AbiVersion">The tree-sitter ABI version the grammar targets (defaults to 14 when unspecified by the manifest).</param>
public sealed record LanguageInfo(
    string Name,
    string Repo,
    string Rev,
    string? Branch,
    string? Directory,
    bool Generate,
    string CSymbol,
    IReadOnlyList<string> Extensions,
    int AbiVersion)
{
    /// <summary>
    /// The logical native-library name for this grammar, as understood by the
    /// TreeSitter native resolver: <c>tree-sitter-&lt;CSymbol&gt;</c>. The resolver
    /// maps this to the platform file (<c>libtree-sitter-&lt;CSymbol&gt;.so</c>,
    /// <c>.dylib</c>, or <c>.dll</c>).
    /// </summary>
    public string NativeLibraryName => "tree-sitter-" + CSymbol;

    /// <summary>
    /// The C entry-point symbol exported by the grammar library:
    /// <c>tree_sitter_&lt;CSymbol&gt;</c>. Invoking it returns the
    /// <c>const TSLanguage*</c> wrapped by <see cref="TreeSitter.Language"/>.
    /// </summary>
    public string ExportSymbol => "tree_sitter_" + CSymbol;
}
