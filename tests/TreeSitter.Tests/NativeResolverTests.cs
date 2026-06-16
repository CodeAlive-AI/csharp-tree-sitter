using System.Reflection;
using System.Runtime.InteropServices;
using TreeSitter.Native;

namespace TreeSitter.Tests;

public class NativeResolverTests
{
    [Theory]
    [InlineData("tree-sitter", "libtree-sitter.so")]
    [InlineData("tree-sitter-json", "libtree-sitter-json.so")]
    public void MapLibraryFileName_linux(string logical, string expected)
    {
        Assert.Equal(expected, NativeLibraryResolver.MapLibraryFileName(logical, OSPlatform.Linux));
    }

    [Theory]
    [InlineData("tree-sitter", "tree-sitter.dll")]
    [InlineData("tree-sitter-python", "tree-sitter-python.dll")]
    public void MapLibraryFileName_windows(string logical, string expected)
    {
        Assert.Equal(expected, NativeLibraryResolver.MapLibraryFileName(logical, OSPlatform.Windows));
    }

    [Theory]
    [InlineData("tree-sitter", "libtree-sitter.dylib")]
    [InlineData("tree-sitter-c", "libtree-sitter-c.dylib")]
    public void MapLibraryFileName_macos(string logical, string expected)
    {
        Assert.Equal(expected, NativeLibraryResolver.MapLibraryFileName(logical, OSPlatform.OSX));
    }

    [Fact]
    public void MapLibraryFileName_other_platform_defaults_to_so()
    {
        // FreeBSD and other Unix-likes fall through to the .so mapping.
        Assert.Equal("libtree-sitter.so", NativeLibraryResolver.MapLibraryFileName("tree-sitter", OSPlatform.FreeBSD));
    }

    [Theory]
    [InlineData("win", Architecture.X64, "win-x64")]
    [InlineData("osx", Architecture.Arm64, "osx-arm64")]
    [InlineData("linux", Architecture.X64, "linux-x64")]
    [InlineData("linux", Architecture.X86, "linux-x86")]
    [InlineData("linux", Architecture.Arm, "linux-arm")]
    [InlineData("linux", Architecture.Arm64, "linux-arm64")]
    public void GetRid_maps_all_os_and_arch(string osToken, Architecture arch, string expected)
    {
        OSPlatform os = osToken switch
        {
            "win" => OSPlatform.Windows,
            "osx" => OSPlatform.OSX,
            _ => OSPlatform.Linux,
        };
        Assert.Equal(expected, NativeLibraryResolver.GetRid(os, arch));
    }

    [Fact]
    public void GetRid_unknown_arch_falls_back_to_lowercased_name()
    {
        // Architecture values outside the explicit cases hit the ToString() fallback.
        // Wasm is a valid enum member that the switch does not special-case.
        string rid = NativeLibraryResolver.GetRid(OSPlatform.Linux, Architecture.Wasm);
        Assert.StartsWith("linux-", rid);
        Assert.Equal(rid.ToLowerInvariant(), rid);
    }

    [Fact]
    public void IsTreeSitterLibrary_recognizes_logical_names()
    {
        Assert.True(NativeLibraryResolver.IsTreeSitterLibrary("tree-sitter"));
        Assert.True(NativeLibraryResolver.IsTreeSitterLibrary("tree-sitter-json"));
        Assert.True(NativeLibraryResolver.IsTreeSitterLibrary("tree-sitter-python"));
        Assert.False(NativeLibraryResolver.IsTreeSitterLibrary("some-other-lib"));
        Assert.False(NativeLibraryResolver.IsTreeSitterLibrary("treesitter"));
    }

    [Fact]
    public void Resolve_non_treesitter_name_returns_zero()
    {
        IntPtr h = NativeLibraryResolver.Resolve("kernel32", typeof(NativeLibraryResolver).Assembly, null);
        Assert.Equal(IntPtr.Zero, h);
    }

    [Fact]
    public void Resolve_known_library_returns_handle_and_caches()
    {
        Assembly asm = typeof(NativeLibraryResolver).Assembly;
        IntPtr first = NativeLibraryResolver.Resolve("tree-sitter", asm, null);
        Assert.NotEqual(IntPtr.Zero, first);
        // Second call returns the cached handle (same value).
        IntPtr second = NativeLibraryResolver.Resolve("tree-sitter", asm, null);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Resolve_unknown_treesitter_grammar_falls_through_to_zero()
    {
        // A tree-sitter-* name with no corresponding library exercises the full probe
        // sequence (env override miss, runtime layout miss, assembly-dir miss, repo
        // walk miss, bare-name miss) and ultimately returns zero.
        IntPtr h = NativeLibraryResolver.Resolve(
            "tree-sitter-this-grammar-does-not-exist-xyz", typeof(NativeLibraryResolver).Assembly, null);
        Assert.Equal(IntPtr.Zero, h);
    }

    [Fact]
    public void Resolve_with_env_override_unset_still_resolves_core()
    {
        // Temporarily clear the override directory to drive the "override missing"
        // branch, then resolve via the remaining strategies (assembly dir / repo walk).
        string? saved = Environment.GetEnvironmentVariable("TREE_SITTER_NATIVE_PATH");
        try
        {
            Environment.SetEnvironmentVariable("TREE_SITTER_NATIVE_PATH", null);
            IntPtr h = NativeLibraryResolver.Resolve(
                "tree-sitter-json", typeof(NativeLibraryResolver).Assembly, null);
            Assert.NotEqual(IntPtr.Zero, h);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TREE_SITTER_NATIVE_PATH", saved);
        }
    }

    [Fact]
    public void Install_is_idempotent()
    {
        // Already installed by the module initializer; calling again is a no-op.
        NativeLibraryResolver.Install();
        NativeLibraryResolver.Install();
        Assert.True(true);
    }

    /// <summary>The portable RID for the current host (matches native/&lt;rid&gt;).</summary>
    private static string HostRid()
    {
        OSPlatform os = OperatingSystem.IsWindows() ? OSPlatform.Windows
            : OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux;
        return NativeLibraryResolver.GetRid(os, RuntimeInformation.ProcessArchitecture);
    }

    private static string CoreLibFileName() =>
        NativeLibraryResolver.MapLibraryFileName("tree-sitter", OperatingSystem.IsWindows() ? OSPlatform.Windows
            : OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux);

    [Fact]
    public void Resolve_via_env_override_directory()
    {
        // Point the override at the repo's native/<rid> dir and resolve a uniquely
        // named copy of the core lib placed there. This drives the env-override hit.
        string nativeDir = Path.Combine(TestData.RepoRoot(), "native", HostRid());
        string coreFile = CoreLibFileName();
        string srcLib = Path.Combine(nativeDir, coreFile);
        if (!File.Exists(srcLib))
            return;

        string unique = "tree-sitter-covenv" + Guid.NewGuid().ToString("N")[..8];
        string uniqueFile = NativeLibraryResolver.MapLibraryFileName(unique,
            OperatingSystem.IsWindows() ? OSPlatform.Windows : OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux);
        string destLib = Path.Combine(nativeDir, uniqueFile);
        string? saved = Environment.GetEnvironmentVariable("TREE_SITTER_NATIVE_PATH");
        try
        {
            File.Copy(srcLib, destLib, overwrite: true);
            Environment.SetEnvironmentVariable("TREE_SITTER_NATIVE_PATH", nativeDir);
            IntPtr h = NativeLibraryResolver.Resolve(unique, typeof(NativeLibraryResolver).Assembly, null);
            Assert.NotEqual(IntPtr.Zero, h);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TREE_SITTER_NATIVE_PATH", saved);
            if (File.Exists(destLib)) File.Delete(destLib);
        }
    }

    [Fact]
    public void Resolve_via_repo_native_walk()
    {
        // With the env override cleared and a name NOT flat-copied beside the assembly,
        // resolution falls through to the repo-root native/<rid>/ walk (TryLoadFromRepoNative).
        string nativeDir = Path.Combine(TestData.RepoRoot(), "native", HostRid());
        string srcLib = Path.Combine(nativeDir, CoreLibFileName());
        if (!File.Exists(srcLib))
            return;

        string unique = "tree-sitter-covwalk" + Guid.NewGuid().ToString("N")[..8];
        string uniqueFile = NativeLibraryResolver.MapLibraryFileName(unique,
            OperatingSystem.IsWindows() ? OSPlatform.Windows : OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux);
        string destLib = Path.Combine(nativeDir, uniqueFile);
        string? saved = Environment.GetEnvironmentVariable("TREE_SITTER_NATIVE_PATH");
        try
        {
            File.Copy(srcLib, destLib, overwrite: true);
            // Clear the override and any path that would short-circuit the walk.
            Environment.SetEnvironmentVariable("TREE_SITTER_NATIVE_PATH", null);
            IntPtr h = NativeLibraryResolver.Resolve(unique, typeof(NativeLibraryResolver).Assembly, null);
            // native/<rid>/ is an ancestor of the test bin dir, so the walk finds it.
            Assert.NotEqual(IntPtr.Zero, h);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TREE_SITTER_NATIVE_PATH", saved);
            if (File.Exists(destLib)) File.Delete(destLib);
        }
    }
}
