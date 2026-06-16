using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TreeSitter.Native;

/// <summary>
/// Installs a <see cref="NativeLibrary.SetDllImportResolver"/> for the TreeSitter
/// assembly that maps the logical native library names used by the P/Invoke layer
/// (<c>"tree-sitter"</c> and <c>"tree-sitter-&lt;lang&gt;"</c>) to the actual
/// platform shared-library files, searching a set of well-known locations.
/// </summary>
/// <remarks>
/// The resolver is wired up exactly once via a <see cref="ModuleInitializerAttribute"/>
/// the first time any code in this assembly runs, so consumers never have to call
/// anything. Resolution order for a logical name:
/// <list type="number">
///   <item><description>The directory named by the <c>TREE_SITTER_NATIVE_PATH</c>
///   environment variable, if set.</description></item>
///   <item><description><c>runtimes/&lt;rid&gt;/native/</c> beneath
///   <see cref="AppContext.BaseDirectory"/> (the standard NuGet runtime layout).</description></item>
///   <item><description><c>NativeLibrary.TryLoad</c> with
///   <see cref="DllImportSearchPath.AssemblyDirectory"/> — the official mechanism to
///   find a library next to the managed assembly (the flat-copy dev/test layout).</description></item>
///   <item><description><c>&lt;repoRoot&gt;/native/&lt;rid&gt;/</c> walking up from
///   the base directory, as a developer fallback when running from a build tree.</description></item>
///   <item><description>A plain <see cref="NativeLibrary.Load(string)"/> of the bare
///   file name, delegating fully to the OS loader (LD_LIBRARY_PATH, ldconfig, etc.).</description></item>
/// </list>
/// Successfully resolved handles are cached by logical name so repeated P/Invoke from
/// different entry points reuses the same load.
/// </remarks>
internal static class NativeLibraryResolver
{
    /// <summary>The logical library name of the core tree-sitter runtime.</summary>
    internal const string CoreLibraryName = "tree-sitter";

    private static int _installed;

    /// <summary>
    /// Cache of resolved native handles keyed by logical library name. The OS loader
    /// already de-duplicates <c>dlopen</c>/<c>LoadLibrary</c> by path, but caching here
    /// avoids re-running the probing sequence (and the file-system stats it performs)
    /// on every distinct P/Invoke entry point.
    /// </summary>
    private static readonly ConcurrentDictionary<string, IntPtr> _handleCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Module initializer: installs the resolver on the TreeSitter assembly the
    /// first time the module is loaded.
    /// </summary>
    /// <remarks>
    /// A module initializer is intentional here: the binding must self-register its
    /// native-library resolver before any P/Invoke runs, with zero wiring required
    /// from consumers. The work is trivial and exception-free, so the usual caveats
    /// behind CA2255 (slow/ordering-sensitive initializers in libraries) do not apply.
    /// </remarks>
#pragma warning disable CA2255 // The ModuleInitializer attribute should not be used in libraries
    [ModuleInitializer]
    internal static void Initialize() => Install();
#pragma warning restore CA2255

    /// <summary>
    /// Idempotently installs the DLL import resolver on the assembly that contains
    /// the P/Invoke declarations.
    /// </summary>
    internal static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);
    }

    /// <summary>
    /// The <see cref="DllImportResolver"/> callback. Returns a handle to the
    /// resolved library, or <see cref="IntPtr.Zero"/> to fall back to the default
    /// runtime resolution.
    /// </summary>
    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        // Only handle the logical names this binding uses. Everything else falls
        // through to the runtime's default resolution.
        if (!IsTreeSitterLibrary(libraryName))
            return IntPtr.Zero;

        // Reuse a previously resolved handle for this logical name when available.
        if (_handleCache.TryGetValue(libraryName, out IntPtr cached))
            return cached;

        IntPtr handle = ResolveCore(libraryName, assembly);

        // Cache only successful resolutions; a zero handle defers to the default
        // resolver (which surfaces a clear DllNotFoundException for the logical name).
        if (handle != IntPtr.Zero)
            _handleCache.TryAdd(libraryName, handle);

        return handle;
    }

    /// <summary>Runs the full probing sequence for an unresolved logical name.</summary>
    private static IntPtr ResolveCore(string libraryName, Assembly assembly)
    {
        string fileName = MapToFileName(libraryName);
        string rid = RuntimeIdentifier;

        // (1) Explicit override directory.
        string? overrideDir = Environment.GetEnvironmentVariable("TREE_SITTER_NATIVE_PATH");
        if (!string.IsNullOrEmpty(overrideDir) &&
            TryLoadFrom(Path.Combine(overrideDir, fileName), out IntPtr handle))
        {
            return handle;
        }

        string baseDir = AppContext.BaseDirectory;

        // (2) Standard NuGet runtime layout: runtimes/<rid>/native/<file>.
        if (TryLoadFrom(Path.Combine(baseDir, "runtimes", rid, "native", fileName), out handle))
            return handle;

        // (3) Next to the managed assembly. DllImportSearchPath.AssemblyDirectory is
        //     the official, cross-platform way to probe the assembly's own directory
        //     (AppContext.BaseDirectory) for the bare file name; this is the normal
        //     path for libraries flat-copied beside TreeSitter.dll (dev/test runs and
        //     project-reference consumers, which do NOT honour runtimes/<rid>/native).
        if (NativeLibrary.TryLoad(fileName, assembly, DllImportSearchPath.AssemblyDirectory, out handle))
            return handle;

        // Belt-and-braces explicit app-base path in case the search above did not
        // consult it on some host configuration.
        if (TryLoadFrom(Path.Combine(baseDir, fileName), out handle))
            return handle;

        // (4) Developer fallback: walk up from the base directory looking for a
        //     repo-root native/<rid>/ folder (e.g. when running from bin/Debug).
        if (TryLoadFromRepoNative(baseDir, rid, fileName, out handle))
            return handle;

        // (5) Last resort: bare-name load straight through the OS loader (honours
        //     LD_LIBRARY_PATH / ldconfig / DYLD_* / PATH and any embedded rpath).
        if (NativeLibrary.TryLoad(fileName, out handle))
            return handle;

        // Give up and let the default resolver try (it will most likely also fail,
        // producing a clear DllNotFoundException for the original logical name).
        return IntPtr.Zero;
    }

    /// <summary>True if the logical name is one this binding is responsible for.</summary>
    private static bool IsTreeSitterLibrary(string libraryName) =>
        libraryName == CoreLibraryName ||
        libraryName.StartsWith("tree-sitter-", StringComparison.Ordinal);

    /// <summary>
    /// Maps a logical library name to the platform-specific file name, e.g.
    /// <c>"tree-sitter"</c> -&gt; <c>libtree-sitter.so</c> on Linux,
    /// <c>tree-sitter.dll</c> on Windows, <c>libtree-sitter.dylib</c> on macOS.
    /// </summary>
    private static string MapToFileName(string logicalName)
    {
        if (OperatingSystem.IsWindows())
            return logicalName + ".dll";
        if (OperatingSystem.IsMacOS())
            return "lib" + logicalName + ".dylib";
        // Linux and other Unix-likes.
        return "lib" + logicalName + ".so";
    }

    private static bool TryLoadFrom(string path, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        try
        {
            if (File.Exists(path))
                return NativeLibrary.TryLoad(path, out handle);
        }
        catch
        {
            // Ignore and let the next strategy run.
        }
        return false;
    }

    /// <summary>
    /// Walks up the directory tree from <paramref name="startDir"/> looking for a
    /// <c>native/&lt;rid&gt;/&lt;file&gt;</c> entry (the repository's built-output
    /// layout), trying both the rid-specific folder and a flat <c>native/</c> folder.
    /// </summary>
    private static bool TryLoadFromRepoNative(string startDir, string rid, string fileName, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        DirectoryInfo? dir = new(startDir);

        // Bound the walk to avoid pathological loops.
        for (int i = 0; i < 12 && dir is not null; i++, dir = dir.Parent)
        {
            string ridPath = Path.Combine(dir.FullName, "native", rid, fileName);
            if (TryLoadFrom(ridPath, out handle))
                return true;

            string flatPath = Path.Combine(dir.FullName, "native", fileName);
            if (TryLoadFrom(flatPath, out handle))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The .NET runtime identifier for the current OS + architecture, used to build
    /// <c>runtimes/&lt;rid&gt;/native</c> and <c>native/&lt;rid&gt;</c> paths.
    /// </summary>
    private static string RuntimeIdentifier
    {
        get
        {
            string os =
                OperatingSystem.IsWindows() ? "win" :
                OperatingSystem.IsMacOS() ? "osx" :
                "linux";

            string arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm64 => "arm64",
                Architecture.Arm => "arm",
                _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            };

            return $"{os}-{arch}";
        }
    }
}

/// <summary>
/// Minimal binding to the C runtime's <c>free</c> so the managed layer can release
/// buffers that tree-sitter allocated with <c>malloc</c> (notably the strings from
/// <c>ts_node_string</c> and the range arrays from <c>ts_tree_included_ranges</c>
/// and <c>ts_tree_get_changed_ranges</c>). These must NOT be released with the GC
/// or <c>Marshal.FreeHGlobal</c>.
/// </summary>
internal static partial class Libc
{
    /// <summary>Releases a buffer previously allocated by tree-sitter via libc <c>malloc</c>.</summary>
    /// <param name="ptr">The pointer to free. A null pointer is ignored.</param>
    internal static void Free(IntPtr ptr)
    {
        if (ptr != IntPtr.Zero)
            FreeCore(ptr);
    }

    // On Windows the C runtime used by tree-sitter (built with MSVC/clang) exposes
    // `free` from the universal CRT (ucrtbase). On Unix it lives in the C library
    // that is already loaded into the process. Using the platform default library
    // name lets the loader find the in-process CRT in both cases.
    private static void FreeCore(IntPtr ptr)
    {
        if (OperatingSystem.IsWindows())
            FreeWindows(ptr);
        else
            FreeUnix(ptr);
    }

    [LibraryImport("ucrtbase", EntryPoint = "free")]
    private static partial void FreeWindows(IntPtr ptr);

    [LibraryImport("libc", EntryPoint = "free")]
    private static partial void FreeUnix(IntPtr ptr);
}
