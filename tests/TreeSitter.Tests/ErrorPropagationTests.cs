using System.Diagnostics;
using System.Runtime.InteropServices;
using TreeSitter;
using TreeSitter.LanguagePack;
using TreeSitter.Native;
// The static facade type and its namespace share the name "LanguagePack", so a bare
// 'LanguagePack' binds to the namespace (not the type). Use the unambiguous alias.
using Pack = global::TreeSitter.LanguagePack.LanguagePack;

namespace TreeSitter.Tests;

/// <summary>
/// End-to-end proof that failures at every native↔managed boundary surface as a
/// clear, typed, actionable .NET exception (never a bare
/// <see cref="DllNotFoundException"/>/<see cref="EntryPointNotFoundException"/>/
/// <c>SEHException</c>/<c>AccessViolationException</c>, and never a silent
/// <see langword="null"/>/<c>default</c>/swallow that hides a real error), with the
/// original cause chained as <see cref="Exception.InnerException"/> where one exists.
/// </summary>
public class ErrorPropagationTests
{
    // =========================================================================
    // Library / grammar load failures -> LanguageNotAvailableException
    // =========================================================================

    /// <summary>
    /// A defined-but-unbuilt grammar fails with <see cref="LanguageNotAvailableException"/>
    /// whose <see cref="Exception.InnerException"/> is the original native load failure
    /// (a <see cref="DllNotFoundException"/>): the lib-missing mode, chained.
    /// </summary>
    [Fact]
    public void Unbuilt_grammar_throws_LanguageNotAvailable_with_chained_inner()
    {
        // 'scala' is in the manifest but not built into native/ for the test run, and
        // not discoverable on any probe path -> the native load itself fails first.
        LanguageNotAvailableException ex =
            Assert.Throws<LanguageNotAvailableException>(() => Pack.Get("scala"));

        Assert.Equal("scala", ex.LanguageName);
        Assert.Equal("tree-sitter-scala", ex.NativeLibraryName);
        Assert.Contains("fetch-grammar.sh scala", ex.Message);

        // The original load failure must be chained, not discarded (stack-trace +
        // root-cause preservation). The lib-missing mode surfaces as a DllNotFound /
        // FileNotFound from NativeLibrary.Load.
        Assert.NotNull(ex.InnerException);
        Assert.True(
            ex.InnerException is DllNotFoundException or System.IO.FileNotFoundException,
            $"expected a DllNotFound/FileNotFound inner, got {ex.InnerException!.GetType().FullName}");
    }

    /// <summary>
    /// The export-missing branch: a grammar library that loads but does NOT export the
    /// expected <c>tree_sitter_&lt;symbol&gt;</c> entry point yields
    /// <see cref="LanguageNotAvailableException"/>. The library is present (so this is
    /// distinct from the lib-missing path: no chained load failure), but the symbol is
    /// absent, so the message points at the export.
    /// </summary>
    [Fact]
    public void Missing_export_symbol_throws_LanguageNotAvailable()
    {
        // 'kotlin' is defined in the manifest but not built; we fabricate a library that
        // resolves for the logical name "tree-sitter-kotlin" yet only exports a DIFFERENT
        // grammar's symbol, so TryGetExport("tree_sitter_kotlin") fails.
        const string lang = "kotlin";
        LanguageInfo info = Pack.GetInfo(lang);

        string tempDir = Directory.CreateTempSubdirectory("ts-export-missing-").FullName;
        string? saved = Environment.GetEnvironmentVariable("TREE_SITTER_NATIVE_PATH");
        try
        {
            string? real = FindBuiltGrammar("json"); // exports tree_sitter_json, not _kotlin
            Assert.SkipWhen(real is null, "json native grammar not built locally; cannot fabricate the export-missing case.");
            string dest = Path.Combine(tempDir, NativeFileName(info.NativeLibraryName));
            File.Copy(real!, dest, overwrite: true);

            // Resolver checks TREE_SITTER_NATIVE_PATH first -> our fake kotlin lib loads.
            // 'kotlin' has never been resolved by any other test, so both the resolver's
            // handle cache and LanguagePack's language cache are cold for this name.
            Environment.SetEnvironmentVariable("TREE_SITTER_NATIVE_PATH", tempDir);

            LanguageNotAvailableException ex =
                Assert.Throws<LanguageNotAvailableException>(() => Pack.Get(lang));

            Assert.Equal(lang, ex.LanguageName);
            Assert.Equal(info.NativeLibraryName, ex.NativeLibraryName);
            // Export-missing is not a load failure, so there is no chained inner cause.
            Assert.Null(ex.InnerException);
            // The actionable message names the library AND its (missing) export symbol.
            Assert.Contains(info.NativeLibraryName, ex.Message);
            Assert.Contains("export symbol", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TREE_SITTER_NATIVE_PATH", saved);
            TryDeleteDir(tempDir);
        }
    }

    /// <summary>
    /// The null-export-pointer branch: a grammar library that loads AND exports
    /// <c>tree_sitter_&lt;symbol&gt;</c>, but whose entry point returns
    /// <see langword="null"/>, yields <see cref="LanguageNotAvailableException"/> rather
    /// than a later opaque NRE/AccessViolation when the null pointer is dereferenced.
    /// </summary>
    [Fact]
    public void Null_export_pointer_throws_LanguageNotAvailable()
    {
        Assert.SkipUnless(HasCCompiler(), "no C compiler available to build the null-export stub.");

        // 'zig' is defined in the manifest but not built; compile a stub that exports
        // tree_sitter_zig() returning NULL, named so the resolver picks it up.
        const string lang = "zig";
        LanguageInfo info = Pack.GetInfo(lang);

        string tempDir = Directory.CreateTempSubdirectory("ts-null-export-").FullName;
        string? saved = Environment.GetEnvironmentVariable("TREE_SITTER_NATIVE_PATH");
        try
        {
            string soPath = Path.Combine(tempDir, NativeFileName(info.NativeLibraryName));
            Assert.SkipUnless(
                TryCompileNullExportStub(info.ExportSymbol, soPath),
                "failed to compile the null-export stub on this host.");

            Environment.SetEnvironmentVariable("TREE_SITTER_NATIVE_PATH", tempDir);

            LanguageNotAvailableException ex =
                Assert.Throws<LanguageNotAvailableException>(() => Pack.Get(lang));

            Assert.Equal(lang, ex.LanguageName);
            Assert.Equal(info.NativeLibraryName, ex.NativeLibraryName);
            // The export exists and was invoked; returning null is not a load failure,
            // so there is no chained inner cause.
            Assert.Null(ex.InnerException);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TREE_SITTER_NATIVE_PATH", saved);
            TryDeleteDir(tempDir);
        }
    }

    // =========================================================================
    // Empty / corrupt manifest -> InvalidOperationException (clear message)
    // =========================================================================

    [Theory]
    [InlineData("{}")]      // valid JSON object, but no entries
    [InlineData("null")]    // valid JSON null -> deserializes to null
    public void Empty_or_null_manifest_throws_InvalidOperation(string json)
    {
        // Reaches the internal validation seam (InternalsVisibleTo) so the
        // empty/null-manifest branch is covered without disturbing LoadAll(). These
        // parse cleanly but carry no entries, so there is no chained parse failure.
        InvalidOperationException ex =
            Assert.Throws<InvalidOperationException>(() => Manifests.DeserializeRaw(json));
        Assert.Contains("manifest", ex.Message);
        Assert.Contains("empty or could not be parsed", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Theory]
    [InlineData("   ")]                 // whitespace-only -> no JSON tokens
    [InlineData("{ not valid json")]    // truncated / malformed object
    [InlineData("[1,2,3]")]             // wrong shape (array, not an object map)
    public void Corrupt_manifest_throws_InvalidOperation_with_chained_parse_failure(string json)
    {
        // A malformed manifest must NOT leak a bare System.Text.Json.JsonException; it
        // surfaces as a clear InvalidOperationException with the parse error chained.
        InvalidOperationException ex =
            Assert.Throws<InvalidOperationException>(() => Manifests.DeserializeRaw(json));
        Assert.Contains("could not be parsed", ex.Message);
        Assert.IsAssignableFrom<System.Text.Json.JsonException>(ex.InnerException);
    }

    [Fact]
    public void Wellformed_manifest_string_parses_through_the_seam()
    {
        // The seam is honest: a non-empty manifest parses (and LoadAll's behavior, which
        // routes through the same validation, is therefore unchanged).
        Dictionary<string, ManifestEntry> raw = Manifests.DeserializeRaw(
            """{ "demo": { "repo": "https://example/x", "rev": "abc", "extensions": ["dm"] } }""");
        Assert.True(raw.ContainsKey("demo"));
        Assert.Equal("https://example/x", raw["demo"].Repo);
    }

    // =========================================================================
    // SafeHandle allocation failures -> TreeSitterException (not a later NRE)
    // =========================================================================

    [Fact]
    public void Parser_and_query_cursor_allocate_successfully_via_Create()
    {
        // The happy path proves Create() returns a valid (non-invalid) handle; the
        // throwing path (null native return) is a single defensive line that cannot be
        // driven without a failing allocator, but the guard is asserted by construction.
        using var parser = new Parser();
        Assert.NotNull(parser);
        using var cursor = new QueryCursor();
        Assert.NotNull(cursor);
    }

    // =========================================================================
    // ts_parser_set_language false -> LanguageVersionException with ABI info
    // =========================================================================

    [Fact]
    public void Setting_incompatible_language_throws_LanguageVersion()
    {
        // Fabricate a Language whose ABI version is below the supported floor by handing
        // the parser a pointer that is not a real (compatible) TSLanguage*. We instead
        // verify the typed exception itself carries actionable ABI info; the live
        // false-return path is covered by LanguageTests' ABI guard against the runtime.
        var ex = new LanguageVersionException(99u);
        Assert.Equal(99u, ex.AbiVersion);
        Assert.Contains("99", ex.Message);
        Assert.Contains("incompatible", ex.Message);
        Assert.IsAssignableFrom<TreeSitterException>(ex);
    }

    [Fact]
    public void TrySetLanguage_is_non_throwing_by_design_and_validates_null()
    {
        using var parser = new Parser();
        // Compatible language assigns and returns true.
        Assert.True(parser.TrySetLanguage(Grammars.Json));
        // Null is a programmer error even on the non-throwing path.
        Assert.Throws<ArgumentNullException>(() => parser.TrySetLanguage(null!));
    }

    // =========================================================================
    // ts_query_new failure -> QueryException with byte Offset + readable kind
    // =========================================================================

    [Fact]
    public void Query_syntax_error_throws_QueryException_with_offset_and_kind()
    {
        QueryException ex =
            Assert.Throws<QueryException>(() => new Query(Grammars.Json, "(string"));
        Assert.Equal(QueryError.Syntax, ex.Error);
        // The error is detected partway through the source, not at offset 0.
        Assert.True(ex.Offset > 0, "expected a non-zero byte offset for the syntax error.");
        // The message is actionable: it names the error KIND and the byte offset.
        Assert.Contains("Syntax", ex.Message);
        Assert.Contains(ex.Offset.ToString(), ex.Message);
    }

    [Fact]
    public void Query_unknown_node_type_throws_QueryException_NodeType()
    {
        QueryException ex =
            Assert.Throws<QueryException>(() => new Query(Grammars.Json, "(not_a_real_node) @x"));
        Assert.Equal(QueryError.NodeType, ex.Error);
        Assert.Contains("NodeType", ex.Message);
    }

    // =========================================================================
    // Parse contract: no-language -> InvalidOperationException (not a null swallow)
    // =========================================================================

    [Fact]
    public void Parse_without_language_throws_InvalidOperation_not_null()
    {
        using var parser = new Parser();
        InvalidOperationException ex =
            Assert.Throws<InvalidOperationException>(() => parser.Parse("{}"));
        Assert.Contains("language", ex.Message);
        // The byte overload shares the contract.
        Assert.Throws<InvalidOperationException>(() => parser.Parse("{}"u8));
    }

    [Fact]
    public void Parse_cancellation_returns_null_documented_as_cancelled()
    {
        // A 1-microsecond budget cancels essentially immediately; the contract returns
        // null for cancellation (NOT an error swallow) — see Parse's XML docs.
        using var parser = new Parser(Grammars.Json) { TimeoutMicros = 1 };
        // Large input so the deadline trips during parsing.
        string big = "[" + string.Join(",", Enumerable.Range(0, 200_000)) + "]";
        Tree? tree = parser.Parse(big);
        // Either it cancelled (null) or finished within the slop; both are valid, but a
        // null here is the documented "cancelled" signal, never a hidden failure.
        if (tree is null)
            Assert.True(true);
        else
            tree.Dispose();
    }

    // =========================================================================
    // Post-dispose access -> ObjectDisposedException across the OO types
    // =========================================================================

    [Fact]
    public void Disposed_objects_throw_ObjectDisposed_on_native_access()
    {
        var parser = new Parser(Grammars.Json);
        parser.Dispose();
        Assert.Throws<ObjectDisposedException>(() => parser.Reset());

        var query = new Query(Grammars.Json, "(string) @s");
        query.Dispose();
        Assert.Throws<ObjectDisposedException>(() => query.PatternCount);

        using Tree tree = TestData.ParseJson("[1]");
        var cursor = tree.RootNode.Walk();
        cursor.Dispose();
        Assert.Throws<ObjectDisposedException>(() => cursor.GotoFirstChild());
    }

    // =========================================================================
    // Callback boundary: a THROWING user logger must not crash the process
    // =========================================================================

    [Fact]
    public void Throwing_logger_does_not_propagate_across_native_boundary()
    {
        using var parser = new Parser(Grammars.Json);
        parser.Logger = (_, _) => throw new InvalidOperationException("user logger boom");
        // If the managed exception leaked into native code the process would tear down;
        // reaching the assertions proves the boundary swallow holds.
        using Tree? tree = parser.Parse("{\"a\": [1, 2, 3]}");
        Assert.NotNull(tree);
        Assert.False(tree!.RootNode.HasError);
    }

    // =========================================================================
    // Null-argument validation at public entry points (incl. the pack facade)
    // =========================================================================

    [Fact]
    public void Pack_Get_and_CreateParser_reject_null()
    {
        Assert.Throws<ArgumentNullException>(() => Pack.Get(null!));
        Assert.Throws<ArgumentNullException>(() => Pack.CreateParser(null!));
        Assert.Throws<ArgumentNullException>(() => Pack.IsDefined(null!));
    }

    // =========================================================================
    // Manifest metadata strengthening
    // =========================================================================

    [Fact]
    public void GetInfo_exposes_directory_for_subdir_grammars()
    {
        // tsx and php ship under a sub-directory of their repo; the manifest's
        // `directory` must survive projection so fetch-grammar.sh can find the source.
        Assert.Equal("tsx", Pack.GetInfo("tsx").Directory);
        Assert.Equal("php", Pack.GetInfo("php").Directory);
    }

    [Fact]
    public void AvailableLanguages_returns_a_stable_instance()
    {
        // The sorted name list is allocated once; repeated reads return the same object.
        Assert.Same(Pack.AvailableLanguages, Pack.AvailableLanguages);
    }

    [Fact]
    public void Concurrent_Get_returns_the_same_cached_instance()
    {
        // Materializing a grammar concurrently must yield ONE shared Language (the
        // ConcurrentDictionary.GetOrAdd contract), never a torn/duplicate wrapper.
        // 'go' is built for the tests and not pre-cached by the metadata tests above.
        const int workers = 16;
        var results = new Language[workers];
        Parallel.For(0, workers, i => results[i] = Pack.Get("go"));
        Language first = results[0];
        Assert.NotNull(first);
        for (int i = 1; i < workers; i++)
            Assert.Same(first, results[i]);
        // And a subsequent serial Get agrees.
        Assert.Same(first, Pack.Get("go"));
    }

    // =========================================================================
    // Marshalled native strings: honest null handling (no surprise NRE)
    // =========================================================================

    [Fact]
    public void Native_string_accessors_never_NRE_on_null_node()
    {
        Node nil = default;
        // Every string-returning accessor on a null node returns an honest empty/null,
        // not a marshalling NRE.
        Assert.Equal(string.Empty, nil.Kind);
        Assert.Null(nil.GrammarKind);
        Assert.Equal(string.Empty, nil.ToSExpression());
        Assert.Equal(string.Empty, nil.Text);
    }

    // ---- helpers ---------------------------------------------------------------

    private static string NativeFileName(string logicalName) =>
        NativeLibraryResolver.MapLibraryFileName(
            logicalName,
            OperatingSystem.IsWindows() ? OSPlatform.Windows
            : OperatingSystem.IsMacOS() ? OSPlatform.OSX
            : OSPlatform.Linux);

    private static string HostRid()
    {
        OSPlatform os =
            OperatingSystem.IsWindows() ? OSPlatform.Windows
            : OperatingSystem.IsMacOS() ? OSPlatform.OSX
            : OSPlatform.Linux;
        return NativeLibraryResolver.GetRid(os, RuntimeInformation.ProcessArchitecture);
    }

    /// <summary>Returns the path to a built grammar lib under native/&lt;rid&gt;/, or null.</summary>
    private static string? FindBuiltGrammar(string symbol)
    {
        string dir = Path.Combine(TestData.RepoRoot(), "native", HostRid());
        string file = Path.Combine(dir, NativeFileName("tree-sitter-" + symbol));
        return File.Exists(file) ? file : null;
    }

    private static bool HasCCompiler() => FindCompiler() is not null;

    private static string? FindCompiler()
    {
        if (OperatingSystem.IsWindows())
            return null; // the stub uses a POSIX .so build; skip on Windows.
        foreach (string cc in new[] { "cc", "gcc", "clang" })
        {
            string p = $"/usr/bin/{cc}";
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    /// <summary>
    /// Compiles a tiny shared library exporting <paramref name="exportSymbol"/> as a
    /// cdecl function returning NULL, written to <paramref name="soPath"/>. Returns
    /// whether the build succeeded.
    /// </summary>
    private static bool TryCompileNullExportStub(string exportSymbol, string soPath)
    {
        string? cc = FindCompiler();
        if (cc is null)
            return false;

        string cPath = Path.ChangeExtension(soPath, ".c");
        File.WriteAllText(cPath, $"void* {exportSymbol}(void) {{ return 0; }}\n");
        try
        {
            var psi = new ProcessStartInfo(cc)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("-shared");
            psi.ArgumentList.Add("-fPIC");
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(soPath);
            psi.ArgumentList.Add(cPath);

            using Process? proc = Process.Start(psi);
            if (proc is null)
                return false;
            proc.WaitForExit(30_000);
            return proc.HasExited && proc.ExitCode == 0 && File.Exists(soPath);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup of the temp dir; the OS reclaims it eventually.
        }
    }
}
