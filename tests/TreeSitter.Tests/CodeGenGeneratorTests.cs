using TreeSitter.CodeGen;

namespace TreeSitter.Tests;

public class NodeTypesGeneratorTests
{
    private static GeneratedGrammar Generate(string language, string rootNs)
    {
        // Prefer the vendored copy (always next to the test assembly) so json/python
        // generator tests are hermetic; fall back to a fetched source for other grammars.
        string vendored = TestData.VendoredNodeTypesPath(language);
        string? json = File.Exists(vendored) ? File.ReadAllText(vendored) : TestData.NodeTypesJson(language);
        Assert.NotNull(json);
        return NodeTypesGenerator.Generate(json!, new GeneratorOptions
        {
            RootNamespace = rootNs,
            LanguageName = language,
        });
    }

    [Fact]
    public void Generate_null_args_throw()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NodeTypesGenerator.Generate(null!, new GeneratorOptions { RootNamespace = "X", LanguageName = "x" }));
        Assert.Throws<ArgumentNullException>(() =>
            NodeTypesGenerator.Generate("[]", null!));
    }

    [Fact]
    public void Generate_json_is_non_empty_with_expected_counts()
    {
        GeneratedGrammar g = Generate("json", "TreeSitter.Grammars.Json");
        GeneratedFile file = Assert.Single(g.Files);
        Assert.Equal("Json.Nodes.g.cs", file.FileName);
        Assert.NotEmpty(file.Source);

        Assert.True(g.SupertypeCount >= 1);   // _value
        Assert.True(g.ConcreteNodeCount >= 5);
        Assert.True(g.AnonUnionCount >= 1);    // escape_sequence | string_content
        Assert.Equal(
            g.ConcreteNodeCount + g.SupertypeCount + g.UnnamedNodeCount + g.AnonUnionCount,
            g.TotalTypeCount);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("python")]
    [InlineData("c")]
    public void Generate_is_deterministic(string language)
    {
        string? json = TestData.NodeTypesJson(language);
        if (json is null)
            return; // grammar source unavailable; skip silently

        var opts = new GeneratorOptions { RootNamespace = "Det.Ns", LanguageName = language };
        string a = NodeTypesGenerator.Generate(json, opts).Files[0].Source;
        string b = NodeTypesGenerator.Generate(json, opts).Files[0].Source;
        Assert.Equal(a, b); // byte-identical across runs
    }

    /// <summary>
    /// Generates a grammar from the VENDORED node-types.json copy (TestData/), so the
    /// drift guard is hermetic and always runs regardless of /tmp grammar sources.
    /// </summary>
    private static GeneratedGrammar GenerateVendored(string language, string rootNs)
    {
        string json = TestData.VendoredNodeTypesJson(language);
        return NodeTypesGenerator.Generate(json, new GeneratorOptions
        {
            RootNamespace = rootNs,
            LanguageName = language,
        });
    }

    [Fact]
    public void Generate_json_matches_checked_in_binding()
    {
        // HERMETIC drift guard: regenerate from the VENDORED node-types.json (the exact
        // input the checked-in binding was produced from) and assert byte-for-byte
        // equality with grammars/TreeSitter.Grammars.Json/Json.Nodes.g.cs. Never depends
        // on /tmp, so it always runs (incl. a clean CI checkout). Also an integration
        // proof that the generated output compiles (the checked-in file is in the build).
        GeneratedGrammar g = GenerateVendored("json", "TreeSitter.Grammars.Json");
        string generated = g.Files[0].Source;

        string checkedInPath = Path.Combine(
            TestData.RepoRoot(), "grammars", "TreeSitter.Grammars.Json", "Json.Nodes.g.cs");
        Assert.True(File.Exists(checkedInPath), $"missing {checkedInPath}");
        string checkedIn = File.ReadAllText(checkedInPath);

        // Normalize line endings before comparison to be robust to git autocrlf.
        Assert.Equal(Normalize(checkedIn), Normalize(generated));
    }

    [Fact]
    public void Generate_python_matches_checked_in_binding()
    {
        // HERMETIC drift guard for the COMPLEX grammar (supertypes, anonymous unions,
        // hashed member names): regenerate from the vendored python node-types.json and
        // assert byte-for-byte equality with the checked-in Python.Nodes.g.cs.
        GeneratedGrammar g = GenerateVendored("python", "TreeSitter.Grammars.Python");
        string generated = g.Files[0].Source;

        string checkedInPath = Path.Combine(
            TestData.RepoRoot(), "grammars", "TreeSitter.Grammars.Python", "Python.Nodes.g.cs");
        Assert.True(File.Exists(checkedInPath), $"missing {checkedInPath}");
        string checkedIn = File.ReadAllText(checkedInPath);

        Assert.Equal(Normalize(checkedIn), Normalize(generated));
    }

    [Fact]
    public void Generate_json_contains_representative_substrings()
    {
        string src = Generate("json", "TreeSitter.Grammars.Json").Files[0].Source;

        // Auto-generated header.
        Assert.Contains("// <auto-generated/>", src);
        Assert.Contains("#nullable enable", src);
        Assert.Contains("using TreeSitter.Typed;", src);

        // A concrete struct.
        Assert.Contains("public readonly partial struct Pair", src);
        // A supertype Match method + Which discriminator.
        Assert.Contains("public readonly partial struct Value", src);
        Assert.Contains("public Variant Which =>", src);
        Assert.Contains("public TResult Match<TResult>(", src);
        // An anonymous union name.
        Assert.Contains("EscapeSequence_StringContent", src);
        // The Symbols sub-namespace for punctuation tokens.
        Assert.Contains("namespace TreeSitter.Grammars.Json.Symbols", src);
        // A required field accessor that can throw.
        Assert.Contains("IncorrectNodeKindException", src);
    }

    [Fact]
    public void Generate_python_has_keyword_escaped_and_supertypes()
    {
        string? json = TestData.NodeTypesJson("python");
        if (json is null)
            return;

        GeneratedGrammar g = NodeTypesGenerator.Generate(json, new GeneratorOptions
        {
            RootNamespace = "Py",
            LanguageName = "python",
        });
        string src = g.Files[0].Source;
        Assert.True(g.SupertypeCount >= 1);
        Assert.Contains("namespace Py", src);
        // Python has many supertypes (e.g. _compound_statement) -> Match/Switch emitted.
        Assert.Contains("public void Switch(", src);
        // Python's grammar contains keyword-like tokens (e.g. 'lambda', 'await').
        Assert.Contains("namespace Py.Unnamed", src);
    }

    [Fact]
    public void Generate_c_compiles_to_source_with_anon_unions()
    {
        string? json = TestData.NodeTypesJson("c");
        if (json is null)
            return;

        GeneratedGrammar g = NodeTypesGenerator.Generate(json, new GeneratorOptions
        {
            RootNamespace = "CLang",
            LanguageName = "c",
        });
        Assert.NotEmpty(g.Files[0].Source);
        // C has multi-type fields -> anonymous unions.
        Assert.True(g.AnonUnionCount >= 1);
        Assert.Contains("namespace CLang.AnonUnions", g.Files[0].Source);
    }

    [Fact]
    public void Generate_empty_array_produces_header_only_file()
    {
        GeneratedGrammar g = NodeTypesGenerator.Generate("[]", new GeneratorOptions
        {
            RootNamespace = "Empty.Ns",
            LanguageName = "empty",
        });
        Assert.Equal(0, g.TotalTypeCount);
        Assert.Equal("Empty.Nodes.g.cs", g.Files[0].FileName);
        Assert.Contains("// <auto-generated/>", g.Files[0].Source);
    }

    [Fact]
    public void Generate_uses_Grammar_filename_for_empty_language_name()
    {
        GeneratedGrammar g = NodeTypesGenerator.Generate("[]", new GeneratorOptions
        {
            RootNamespace = "X",
            LanguageName = "",
        });
        Assert.Equal("Grammar.Nodes.g.cs", g.Files[0].FileName);
    }

    [Fact]
    public void GeneratorOptions_and_GeneratedFile_records()
    {
        var opts = new GeneratorOptions { RootNamespace = "A.B", LanguageName = "lang" };
        Assert.Equal("A.B", opts.RootNamespace);
        Assert.Equal("lang", opts.LanguageName);

        var f = new GeneratedFile("F.cs", "src");
        Assert.Equal("F.cs", f.FileName);
        Assert.Equal("src", f.Source);
        Assert.Equal(new GeneratedFile("F.cs", "src"), f);
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n");
}
