using TreeSitter;
using TreeSitter.LanguagePack;
// The static facade type and its namespace share the name "LanguagePack", so a bare
// 'LanguagePack' binds to the namespace (not the type). Call the facade through the
// unambiguous 'Pack' alias.
using Pack = global::TreeSitter.LanguagePack.LanguagePack;

namespace TreeSitter.Tests;

/// <summary>
/// Tests for the <see cref="Pack"/> by-name grammar loader: manifest metadata,
/// extension resolution, native loading + parsing, caching, and error paths.
/// </summary>
public class LanguagePackTests
{
    // Grammars that are actually built into native/<rid>/ for the test run. Each row:
    // language name, a snippet, the expected root-node kind. (Name/AbiVersion are
    // asserted generically below — ABI 14 grammars report a null Name.)
    public static IEnumerable<object[]> BuiltGrammars()
    {
        yield return ["json", "{\"a\":1}", "document"];
        yield return ["python", "def f(): pass", "module"];
        yield return ["c", "int main(){return 0;}", "translation_unit"];
        yield return ["go", "package main", "source_file"];
        yield return ["rust", "fn main(){}", "source_file"];
        yield return ["javascript", "const x = 1;", "program"];
        yield return ["cpp", "int x = 0;", "translation_unit"];
        yield return ["bash", "echo hi", "program"];
        yield return ["typescript", "let x: number = 1;", "program"];
        yield return ["tsx", "const x = 1;", "program"];
        yield return ["ruby", "def f; end", "program"];
        yield return ["html", "<div></div>", "document"];
        yield return ["css", "a{color:red}", "stylesheet"];
        yield return ["lua", "local x = 1", "chunk"];
        yield return ["toml", "x = 1", "document"];
        yield return ["yaml", "a: 1", "stream"];
    }

    // ---- Metadata --------------------------------------------------------------

    [Fact]
    public void AvailableLanguages_has_the_full_manifest()
    {
        // The upstream tree-sitter-language-pack manifest pins 306 grammars.
        Assert.Equal(306, Pack.AvailableLanguages.Count);
    }

    [Fact]
    public void AvailableLanguages_is_sorted_and_contains_known_keys()
    {
        var list = Pack.AvailableLanguages.ToList();
        Assert.Equal(list.OrderBy(static k => k, StringComparer.Ordinal), list);
        Assert.Contains("python", list);
        Assert.Contains("csharp", list);
        Assert.Contains("typescript", list);
    }

    [Fact]
    public void IsDefined_distinguishes_known_and_unknown()
    {
        Assert.True(Pack.IsDefined("python"));
        Assert.True(Pack.IsDefined("csharp"));
        Assert.False(Pack.IsDefined("definitely-not-a-language"));
    }

    [Fact]
    public void GetInfo_directory_override_typescript()
    {
        LanguageInfo ts = Pack.GetInfo("typescript");
        Assert.Equal("typescript", ts.Name);
        Assert.Equal("typescript", ts.Directory);
        Assert.Equal("typescript", ts.CSymbol);
        Assert.Equal("tree-sitter-typescript", ts.NativeLibraryName);
        Assert.Equal("tree_sitter_typescript", ts.ExportSymbol);
        Assert.Contains("ts", ts.Extensions);
        Assert.StartsWith("https://github.com/", ts.Repo);
        Assert.False(string.IsNullOrEmpty(ts.Rev));
    }

    [Fact]
    public void GetInfo_csymbol_override_csharp()
    {
        LanguageInfo cs = Pack.GetInfo("csharp");
        Assert.Equal("c_sharp", cs.CSymbol);
        Assert.Equal("tree-sitter-c_sharp", cs.NativeLibraryName);
        Assert.Equal("tree_sitter_c_sharp", cs.ExportSymbol);
        Assert.True(cs.Generate);
        Assert.Contains("cs", cs.Extensions);
    }

    [Fact]
    public void GetInfo_generate_flag_sql()
    {
        LanguageInfo sql = Pack.GetInfo("sql");
        Assert.True(sql.Generate);
        // sql has no c_symbol override, so it defaults to the name.
        Assert.Equal("sql", sql.CSymbol);
    }

    [Fact]
    public void GetInfo_defaults_csymbol_to_name_and_abi_to_14()
    {
        LanguageInfo json = Pack.GetInfo("json");
        Assert.Equal("json", json.CSymbol);
        Assert.Equal(14, json.AbiVersion); // manifest does not pin an abi for json
        Assert.False(json.Generate);
        Assert.Null(json.Directory);
    }

    [Fact]
    public void GetInfo_unknown_throws_with_helpful_message()
    {
        KeyNotFoundException ex =
            Assert.Throws<KeyNotFoundException>(() => Pack.GetInfo("nope-lang"));
        Assert.Contains("nope-lang", ex.Message);
        Assert.Contains("manifest", ex.Message);
    }

    [Fact]
    public void TryGetInfo_returns_false_for_unknown()
    {
        Assert.False(Pack.TryGetInfo("nope-lang", out LanguageInfo? info));
        Assert.Null(info);
    }

    [Fact]
    public void TryGetInfo_returns_true_for_known()
    {
        Assert.True(Pack.TryGetInfo("python", out LanguageInfo? info));
        Assert.NotNull(info);
        Assert.Equal("python", info!.Name);
    }

    // ---- Extension resolution --------------------------------------------------

    [Theory]
    [InlineData(".py")]
    [InlineData("py")]
    [InlineData("PY")]
    [InlineData(" .Py ")]
    public void FindByExtension_resolves_python(string ext)
    {
        Assert.Equal("python", Pack.FindByExtension(ext));
    }

    [Fact]
    public void FindByExtension_unknown_returns_null()
    {
        Assert.Null(Pack.FindByExtension(".nope"));
        Assert.Null(Pack.FindByExtension(""));
    }

    [Fact]
    public void FindAllByExtension_ambiguous_h_lists_c_cpp_objc()
    {
        // tree-sitter-c claims ".h" and lists ambiguous {"h": ["cpp","objc"]}, so the
        // primary owner (c) comes first, then the ambiguous alternatives alphabetically.
        IReadOnlyList<string> langs = Pack.FindAllByExtension("h");
        Assert.Equal("c", langs[0]);
        Assert.Contains("cpp", langs);
        Assert.Contains("objc", langs);
    }

    [Fact]
    public void FindAllByExtension_unknown_is_empty()
    {
        Assert.Empty(Pack.FindAllByExtension("not-an-ext"));
    }

    [Fact]
    public void FindByExtension_prefers_primary_owner_for_ambiguous()
    {
        // The single best answer for ".h" is its primary owner, "c".
        Assert.Equal("c", Pack.FindByExtension(".h"));
    }

    // ---- Loading + parsing -----------------------------------------------------

    [Theory]
    [MemberData(nameof(BuiltGrammars))]
    public void Get_loads_language_with_expected_metadata(string name, string snippet, string expectedRootKind)
    {
        _ = snippet;
        _ = expectedRootKind;
        Language lang = Pack.Get(name);
        Assert.NotNull(lang);

        LanguageInfo info = Pack.GetInfo(name);
        // The native grammar must be ABI-compatible with the bundled runtime.
        Assert.InRange(
            lang.AbiVersion,
            TreeSitterConstants.MinCompatibleAbiVersion,
            TreeSitterConstants.AbiVersion);

        // ABI 15+ grammars expose their name; it must match the c_symbol stem.
        if (lang.Name is not null)
            Assert.Equal(info.CSymbol, lang.Name);
    }

    [Theory]
    [MemberData(nameof(BuiltGrammars))]
    public void CreateParser_parses_snippet_to_expected_root(string name, string snippet, string expectedRootKind)
    {
        using Parser parser = Pack.CreateParser(name);
        using Tree? tree = parser.Parse(snippet);
        Assert.NotNull(tree);
        Node root = tree!.RootNode;
        Assert.Equal(expectedRootKind, root.Kind);
        Assert.False(root.HasError);
    }

    [Fact]
    public void Get_caches_the_same_instance()
    {
        Language a = Pack.Get("python");
        Language b = Pack.Get("python");
        Assert.Same(a, b);
    }

    [Fact]
    public void TryGet_succeeds_for_built_grammar()
    {
        Assert.True(Pack.TryGet("python", out Language? lang));
        Assert.NotNull(lang);
        Assert.Same(Pack.Get("python"), lang);
    }

    // ---- Error paths -----------------------------------------------------------

    [Fact]
    public void Get_throws_KeyNotFound_for_unknown_language()
    {
        Assert.Throws<KeyNotFoundException>(() => Pack.Get("not-a-language"));
    }

    [Fact]
    public void Get_throws_LanguageNotAvailable_for_unbuilt_grammar()
    {
        // 'scala' is defined in the manifest but not built into native/ for the tests.
        LanguageNotAvailableException ex =
            Assert.Throws<LanguageNotAvailableException>(() => Pack.Get("scala"));
        Assert.Equal("scala", ex.LanguageName);
        Assert.Equal("tree-sitter-scala", ex.NativeLibraryName);
        Assert.Contains("fetch-grammar.sh scala", ex.Message);
    }

    [Fact]
    public void TryGet_returns_false_for_unbuilt_grammar()
    {
        Assert.False(Pack.TryGet("scala", out Language? lang));
        Assert.Null(lang);
    }

    [Fact]
    public void TryGet_returns_false_for_unknown_language()
    {
        Assert.False(Pack.TryGet("not-a-language", out Language? lang));
        Assert.Null(lang);
    }

    [Fact]
    public void CreateParser_unknown_throws_KeyNotFound()
    {
        Assert.Throws<KeyNotFoundException>(() => Pack.CreateParser("not-a-language"));
    }

    // ---- Argument validation ---------------------------------------------------

    [Fact]
    public void Public_methods_reject_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => Pack.IsDefined(null!));
        Assert.Throws<ArgumentNullException>(() => Pack.GetInfo(null!));
        Assert.Throws<ArgumentNullException>(() => Pack.TryGetInfo(null!, out _));
        Assert.Throws<ArgumentNullException>(() => Pack.TryGet(null!, out _));
        Assert.Throws<ArgumentNullException>(() => Pack.FindByExtension(null!));
        Assert.Throws<ArgumentNullException>(() => Pack.FindAllByExtension(null!));
    }
}

/// <summary>
/// White-box tests of the internal manifest helpers (the index builder's edge cases and
/// the extension normalizer), reached via InternalsVisibleTo.
/// </summary>
public class LanguagePackInternalsTests
{
    [Theory]
    [InlineData(".py", "py")]
    [InlineData("py", "py")]
    [InlineData("  .CS ", "cs")]
    [InlineData("", "")]
    [InlineData(".", "")]
    public void NormalizeExtension_strips_dot_trims_and_lowercases(string input, string expected)
    {
        Assert.Equal(expected, Pack.NormalizeExtension(input));
    }

    private static LanguageInfo Info(string name, params string[] exts) =>
        new(name, "repo", "rev", null, null, false, name, exts, 14);

    [Fact]
    public void BuildExtensionIndexCore_orders_primary_then_secondary()
    {
        var manifest = new Dictionary<string, LanguageInfo>(StringComparer.Ordinal)
        {
            ["c"] = Info("c", "h"),
            ["cpp"] = Info("cpp", "cc"),
            ["objc"] = Info("objc", "m"),
        };
        // c primarily owns "h"; cpp and objc are ambiguous alternatives for "h".
        var ambiguous = new[]
        {
            new KeyValuePair<string, IReadOnlyList<string>>("h", new[] { "cpp", "objc" }),
        };

        var index = Manifests.BuildExtensionIndexCore(manifest, ambiguous);
        Assert.Equal(new[] { "c", "cpp", "objc" }, index["h"]);
    }

    [Fact]
    public void BuildExtensionIndexCore_skips_unknown_and_already_primary_alternatives()
    {
        var manifest = new Dictionary<string, LanguageInfo>(StringComparer.Ordinal)
        {
            // "x" claims ext "e" with an EMPTY extension entry mixed in (covers the
            // empty-extension skip), and "y" also claims "e" (so it is already primary).
            ["x"] = Info("x", "e", ""),
            ["y"] = Info("y", "e"),
        };
        var ambiguous = new[]
        {
            // "y" is already a primary owner of "e" -> skipped (not duplicated).
            // "ghost" is not in the manifest -> skipped.
            new KeyValuePair<string, IReadOnlyList<string>>("e", new[] { "y", "ghost" }),
        };

        var index = Manifests.BuildExtensionIndexCore(manifest, ambiguous);
        Assert.Equal(new[] { "x", "y" }, index["e"]);
        Assert.DoesNotContain("ghost", index["e"]);
        // The empty extension contributed no key.
        Assert.False(index.ContainsKey(string.Empty));
    }
}
