using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TreeSitter;

namespace TreeSitter.Grammars.Json;

/// <summary>
/// Provides the tree-sitter <see cref="TreeSitter.Language"/> for the JSON grammar.
/// The native entry point <c>tree_sitter_json</c> is exported by
/// <c>libtree-sitter-json</c>; this type loads it on demand and caches the result.
/// </summary>
public static partial class JsonLanguage
{
    private static Language? _language;
    private static readonly Lock Gate = new();

    /// <summary>The logical native library name (mapped to the platform file by the resolver).</summary>
    private const string LibraryName = "tree-sitter-json";

    /// <summary>
    /// The JSON <see cref="TreeSitter.Language"/>. The underlying <c>TSLanguage*</c>
    /// is statically allocated by the grammar and lives for the process lifetime;
    /// this <see cref="TreeSitter.Language"/> wrapper is created once and cached.
    /// </summary>
    public static Language Language
    {
        get
        {
            Language? cached = _language;
            if (cached is not null)
                return cached;

            lock (Gate)
            {
                _language ??= new Language(tree_sitter_json());
                return _language;
            }
        }
    }

    [LibraryImport(LibraryName)]
    private static partial nint tree_sitter_json();

    // The core TreeSitter assembly installs a DllImportResolver on itself; that does
    // not extend to this assembly's P/Invokes. Install an equivalent resolver here so
    // libtree-sitter-json is found via TREE_SITTER_NATIVE_PATH, the NuGet
    // runtimes/<rid>/native layout, next to this assembly (dev copy), or the OS loader.
#pragma warning disable CA2255 // ModuleInitializer in libraries — intentional, trivial, exception-free.
    [ModuleInitializer]
    internal static void RegisterResolver() =>
        NativeLibrary.SetDllImportResolver(typeof(JsonLanguage).Assembly, Resolve);
#pragma warning restore CA2255

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibraryName)
            return nint.Zero;

        string file = MapToFileName(libraryName);

        string? overrideDir = Environment.GetEnvironmentVariable("TREE_SITTER_NATIVE_PATH");
        if (!string.IsNullOrEmpty(overrideDir) &&
            TryLoad(Path.Combine(overrideDir, file), out nint handle))
        {
            return handle;
        }

        string baseDir = AppContext.BaseDirectory;
        if (TryLoad(Path.Combine(baseDir, "runtimes", Rid, "native", file), out handle))
            return handle;
        if (NativeLibrary.TryLoad(file, assembly, DllImportSearchPath.AssemblyDirectory, out handle))
            return handle;
        if (TryLoad(Path.Combine(baseDir, file), out handle))
            return handle;
        if (NativeLibrary.TryLoad(file, out handle))
            return handle;

        return nint.Zero;
    }

    private static bool TryLoad(string path, out nint handle)
    {
        handle = nint.Zero;
        try
        {
            return File.Exists(path) && NativeLibrary.TryLoad(path, out handle);
        }
        catch
        {
            return false;
        }
    }

    private static string MapToFileName(string logical) =>
        OperatingSystem.IsWindows() ? logical + ".dll" :
        OperatingSystem.IsMacOS() ? "lib" + logical + ".dylib" :
        "lib" + logical + ".so";

    private static string Rid
    {
        get
        {
            string os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
            string arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                Architecture.X86 => "x86",
                Architecture.Arm => "arm",
                _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            };
            return $"{os}-{arch}";
        }
    }
}
