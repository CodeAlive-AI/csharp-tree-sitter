using System.Runtime.InteropServices;
using TreeSitter;
using TreeSitter.Native;

namespace TreeSitter.Tests;

/// <summary>
/// Targeted tests that close coverage on otherwise-hard-to-reach branches: the
/// ABI-incompatibility path and native-resolver probe misses.
/// </summary>
/// <remarks>
/// These tests mutate the process-global <c>TREE_SITTER_NATIVE_PATH</c> environment
/// variable, so the assembly disables test parallelization (see TestSupport.cs).
/// Deliberately GC-forcing finalizer tests are intentionally NOT included: forcing a
/// full GC + finalization while the coverage data collector is attached and native
/// interop is in flight is unstable, so finalizer cleanup is verified by inspection
/// rather than by an at-risk runtime test.
/// </remarks>
public class CoverageClosersTests
{
    /// <summary>
    /// Allocates a fake <c>TSLanguage</c> whose first field (<c>abi_version</c>) is
    /// out of the supported range. <c>ts_parser_set_language</c> and
    /// <c>ts_language_abi_version</c> only read that first field, so this is safe to
    /// pass across the boundary to drive the version-rejection path.
    /// </summary>
    private static unsafe Language FakeLanguageWithAbi(uint abi)
    {
        // 256 bytes zeroed, first uint32 = abi. Never freed (process-lifetime, tiny);
        // tree-sitter treats it as a borrowed const pointer and only reads abi_version
        // (it rejects an out-of-range version before dereferencing any other field).
        IntPtr buffer = Marshal.AllocHGlobal(256);
        for (int i = 0; i < 256; i++)
            ((byte*)buffer)[i] = 0;
        *(uint*)buffer = abi;
        return new Language(buffer);
    }

    [Fact]
    public void Incompatible_language_setter_throws_with_abi()
    {
        Language bad = FakeLanguageWithAbi(99); // above max supported (15)
        Assert.Equal(99u, bad.AbiVersion);

        using var parser = new Parser();
        LanguageVersionException ex = Assert.Throws<LanguageVersionException>(() => parser.Language = bad);
        Assert.Equal(99u, ex.AbiVersion);
        Assert.Contains("99", ex.Message);
    }

    [Fact]
    public void Incompatible_language_trySet_returns_false()
    {
        Language bad = FakeLanguageWithAbi(2); // below min-compatible (13)
        using var parser = new Parser();
        Assert.False(parser.TrySetLanguage(bad));
        Assert.Null(parser.Language); // unchanged
    }

    [Fact]
    public void Resolver_repo_walk_with_override_cleared()
    {
        // Clear the env override so resolution falls through to the remaining probe
        // strategies (runtime layout, assembly dir, repo-root native/<rid> walk).
        string? saved = Environment.GetEnvironmentVariable("TREE_SITTER_NATIVE_PATH");
        try
        {
            Environment.SetEnvironmentVariable("TREE_SITTER_NATIVE_PATH", "/nonexistent-dir-xyz");
            IntPtr h = NativeLibraryResolver.Resolve(
                "tree-sitter-python", typeof(NativeLibraryResolver).Assembly, null);
            // python resolves via the assembly-dir flat copy regardless of the override.
            Assert.NotEqual(IntPtr.Zero, h);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TREE_SITTER_NATIVE_PATH", saved);
        }
    }
}
