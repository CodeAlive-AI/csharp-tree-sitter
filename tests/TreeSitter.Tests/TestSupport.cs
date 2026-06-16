using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TreeSitter;

// Run tests sequentially. Several tests touch PROCESS-GLOBAL state that is unsafe to
// exercise concurrently: they mutate the TREE_SITTER_NATIVE_PATH environment variable
// (native-resolver tests) and force full GC + finalization (finalizer-coverage tests).
// Both would race against the many tests that load grammars / use native handles. The
// suite is fast (~1s), so serial execution is the correct, deterministic choice.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TreeSitter.Tests;

/// <summary>
/// Sets <c>TREE_SITTER_NATIVE_PATH</c> to the test output directory before any
/// P/Invoke runs, so the native resolver finds the flat-copied native libs no matter
/// what the working directory is. Idempotent and runs once per test assembly load.
/// </summary>
internal static class TestModuleInitializer
{
    [ModuleInitializer]
    internal static void Init()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TREE_SITTER_NATIVE_PATH")))
        {
            string dir = AppContext.BaseDirectory;
            if (Directory.Exists(dir))
                Environment.SetEnvironmentVariable("TREE_SITTER_NATIVE_PATH", dir);
        }
    }
}

/// <summary>
/// Loads tree-sitter <see cref="Language"/> objects for grammars whose native libs
/// are copied next to the test assembly. The core resolver (installed by the
/// TreeSitter assembly) already knows how to find <c>tree-sitter-&lt;name&gt;</c>, so
/// we trigger it by P/Invoking the grammar's <c>tree_sitter_&lt;name&gt;</c> export.
/// Results are cached; languages are statically allocated and never disposed.
/// </summary>
internal static class Grammars
{
    private static readonly Dictionary<string, Language> Cache = new(StringComparer.Ordinal);
    private static readonly Lock Gate = new();

    [DllImport("tree-sitter-json", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr tree_sitter_json();

    [DllImport("tree-sitter-python", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr tree_sitter_python();

    [DllImport("tree-sitter-c", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr tree_sitter_c();

    [DllImport("tree-sitter-go", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr tree_sitter_go();

    [DllImport("tree-sitter-rust", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr tree_sitter_rust();

    [DllImport("tree-sitter-javascript", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr tree_sitter_javascript();

    private static Language Get(string name, Func<IntPtr> entry)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(name, out Language? cached))
                return cached;
            var lang = new Language(entry());
            Cache[name] = lang;
            return lang;
        }
    }

    public static Language Json => Get("json", tree_sitter_json);
    public static Language Python => Get("python", tree_sitter_python);
    public static Language C => Get("c", tree_sitter_c);
    public static Language Go => Get("go", tree_sitter_go);
    public static Language Rust => Get("rust", tree_sitter_rust);
    public static Language JavaScript => Get("javascript", tree_sitter_javascript);
}

/// <summary>
/// Convenience helpers for the tests: locate grammar <c>node-types.json</c> sources
/// and build small parsers/trees.
/// </summary>
internal static class TestData
{
    /// <summary>
    /// Candidate roots that may hold <c>&lt;lang&gt;/src/node-types.json</c>. Includes
    /// the <c>TREE_SITTER_GRAMMARS</c> env override (if set), the fetch scripts' clone
    /// cache (<c>/tmp/ts-grammars</c>, the default <c>CACHE_DIR</c> of
    /// <c>fetch-grammar.sh</c>/<c>fetch-test-grammars.sh</c>), a legacy
    /// out-of-band location (<c>/tmp/grammars</c>), and the submodule tree. This makes
    /// the determinism/CLI tests actually execute in CI (which clones to
    /// <c>/tmp/ts-grammars</c>) rather than silently no-op.
    /// </summary>
    private static readonly string[] GrammarRoots = BuildGrammarRoots();

    private static string[] BuildGrammarRoots()
    {
        var roots = new List<string>();
        string? envOverride = Environment.GetEnvironmentVariable("TREE_SITTER_GRAMMARS");
        if (!string.IsNullOrEmpty(envOverride))
            roots.Add(envOverride);
        roots.Add("/tmp/ts-grammars");
        roots.Add("/tmp/grammars");
        roots.Add(Path.Combine(RepoRoot(), "tree-sitter"));
        return roots.ToArray();
    }

    /// <summary>Returns the repository root by walking up from the test assembly.</summary>
    public static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir is not null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TreeSitter.slnx")))
                return dir.FullName;
        }
        return AppContext.BaseDirectory;
    }

    /// <summary>
    /// Returns the path to a grammar's <c>node-types.json</c>, or <see langword="null"/>
    /// if it cannot be found in any known location.
    /// </summary>
    public static string? NodeTypesPath(string language)
    {
        foreach (string root in GrammarRoots)
        {
            string p = Path.Combine(root, language, "src", "node-types.json");
            if (File.Exists(p))
                return p;
            // tree-sitter-<lang> layout
            string p2 = Path.Combine(root, "tree-sitter-" + language, "src", "node-types.json");
            if (File.Exists(p2))
                return p2;
        }
        return null;
    }

    /// <summary>Reads a grammar's node-types.json text, or returns null if unavailable.</summary>
    public static string? NodeTypesJson(string language)
    {
        string? p = NodeTypesPath(language);
        return p is null ? null : File.ReadAllText(p);
    }

    /// <summary>
    /// Path to a VENDORED grammar <c>node-types.json</c> copied next to the test
    /// assembly (<c>TestData/&lt;lang&gt;.node-types.json</c>). Unlike
    /// <see cref="NodeTypesPath(string)"/> this never depends on /tmp or the submodule
    /// tree, so the hermetic drift-guard tests always run.
    /// </summary>
    public static string VendoredNodeTypesPath(string language) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", $"{language}.node-types.json");

    /// <summary>Reads a vendored grammar's <c>node-types.json</c> text.</summary>
    public static string VendoredNodeTypesJson(string language) =>
        File.ReadAllText(VendoredNodeTypesPath(language));

    /// <summary>Creates a parser bound to the JSON grammar.</summary>
    public static Parser JsonParser() => new(Grammars.Json);

    /// <summary>Parses JSON source into a tree (caller disposes).</summary>
    public static Tree ParseJson(string source)
    {
        using var parser = JsonParser();
        Tree? tree = parser.Parse(source);
        Assert.NotNull(tree);
        return tree!;
    }
}
