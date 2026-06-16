using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TreeSitter.LanguagePack;

/// <summary>
/// Loads and parses the embedded <c>language_definitions.json</c> manifest into the
/// immutable lookups used by <see cref="LanguagePack"/>.
/// </summary>
internal static class Manifests
{
    /// <summary>Resource name of the embedded manifest (assembly-qualified).</summary>
    private const string ResourceName = "TreeSitter.LanguagePack.language_definitions.json";

    /// <summary>
    /// Reads and deserializes the embedded manifest <b>once</b>, returning both the
    /// projected <c>name -&gt; LanguageInfo</c> dictionary and the extension index built
    /// from the same parse. Sharing a single deserialization avoids reading and parsing
    /// the (large) embedded JSON twice at startup.
    /// </summary>
    internal static (FrozenDictionary<string, LanguageInfo> Manifest,
        FrozenDictionary<string, IReadOnlyList<string>> ByExtension) LoadAll()
    {
        Dictionary<string, ManifestEntry> raw = DeserializeRaw();

        var result = new Dictionary<string, LanguageInfo>(raw.Count, StringComparer.Ordinal);
        foreach ((string name, ManifestEntry entry) in raw)
        {
            result[name] = new LanguageInfo(
                Name: name,
                Repo: entry.Repo ?? string.Empty,
                Rev: entry.Rev ?? string.Empty,
                Branch: entry.Branch,
                Directory: entry.Directory,
                Generate: entry.Generate ?? false,
                CSymbol: string.IsNullOrEmpty(entry.CSymbol) ? name : entry.CSymbol,
                Extensions: entry.Extensions ?? [],
                AbiVersion: entry.AbiVersion ?? DefaultAbiVersion);
        }

        FrozenDictionary<string, LanguageInfo> manifest = result.ToFrozenDictionary(StringComparer.Ordinal);
        FrozenDictionary<string, IReadOnlyList<string>> byExtension =
            BuildExtensionIndexCore(manifest, EnumerateAmbiguous(raw));
        return (manifest, byExtension);
    }

    /// <summary>The default ABI version when the manifest does not specify one.</summary>
    internal const int DefaultAbiVersion = 14;

    /// <summary>Deserializes the embedded manifest into its raw entries, throwing if empty.</summary>
    private static Dictionary<string, ManifestEntry> DeserializeRaw()
    {
        using Stream stream = OpenManifest();
        return DeserializeRaw(stream);
    }

    /// <summary>
    /// Parses manifest JSON from an arbitrary <paramref name="stream"/> into its raw
    /// entries, throwing <see cref="InvalidOperationException"/> with a clear message
    /// when the document is empty, <c>null</c>, or contains no entries. This is the
    /// single validation seam shared by the embedded-resource read and the tests; it
    /// keeps the public <see cref="LoadAll"/> behavior identical while letting the
    /// empty/corrupt-manifest path be exercised with synthetic input.
    /// </summary>
    /// <param name="stream">A UTF-8 JSON stream holding a <c>name -&gt; entry</c> object.</param>
    internal static Dictionary<string, ManifestEntry> DeserializeRaw(Stream stream)
    {
        Dictionary<string, ManifestEntry>? raw;
        try
        {
            raw = JsonSerializer.Deserialize(stream, ManifestJsonContext.Default.DictionaryStringManifestEntry);
        }
        catch (JsonException ex)
        {
            // A malformed manifest must surface as a clear, typed error with the parse
            // failure chained — never a bare JsonException leaking out of the loader.
            throw new InvalidOperationException(
                "The embedded language manifest is empty or could not be parsed.", ex);
        }

        if (raw is null || raw.Count == 0)
            throw new InvalidOperationException("The embedded language manifest is empty or could not be parsed.");

        return raw;
    }

    /// <summary>
    /// Parses manifest JSON from a <paramref name="json"/> string. A convenience wrapper
    /// over <see cref="DeserializeRaw(Stream)"/> for tests; shares the same validation.
    /// </summary>
    /// <param name="json">The manifest JSON text.</param>
    internal static Dictionary<string, ManifestEntry> DeserializeRaw(string json)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        return DeserializeRaw(stream);
    }

    /// <summary>
    /// Pure index builder, separated from the embedded-resource read so it can be unit
    /// tested with synthetic manifests and ambiguity maps. Each extension (lowercase, no
    /// dot) maps to its primary owners (by their own <c>extensions</c>), in ordinal name
    /// order, followed by ambiguous alternatives that are not already primary owners.
    /// </summary>
    internal static FrozenDictionary<string, IReadOnlyList<string>> BuildExtensionIndexCore(
        IReadOnlyDictionary<string, LanguageInfo> manifest,
        IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> ambiguous)
    {
        // Primary owners: languages whose own `extensions` list contains the extension.
        var primary = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        // Secondary: extension -> language, contributed via another language's `ambiguous`.
        var secondary = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (LanguageInfo info in manifest.Values)
        {
            foreach (string ext in info.Extensions)
            {
                string key = ext.ToLowerInvariant();
                if (key.Length == 0)
                    continue;
                (primary.TryGetValue(key, out SortedSet<string>? set)
                    ? set
                    : primary[key] = new SortedSet<string>(StringComparer.Ordinal)).Add(info.Name);
            }
        }

        // Pull in `ambiguous` alternatives (these names are not on the alternative
        // language's own `extensions` list, so add them as secondary).
        foreach ((string ext, IReadOnlyList<string> alts) in ambiguous)
        {
            string key = ext.ToLowerInvariant();
            foreach (string alt in alts)
            {
                if (!manifest.ContainsKey(alt))
                    continue;
                // Only add as secondary if it is not already a primary owner of this ext.
                if (primary.TryGetValue(key, out SortedSet<string>? p) && p.Contains(alt))
                    continue;
                (secondary.TryGetValue(key, out SortedSet<string>? set)
                    ? set
                    : secondary[key] = new SortedSet<string>(StringComparer.Ordinal)).Add(alt);
            }
        }

        var combined = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (string key in primary.Keys.Union(secondary.Keys))
        {
            var list = new List<string>();
            if (primary.TryGetValue(key, out SortedSet<string>? p))
                list.AddRange(p);
            if (secondary.TryGetValue(key, out SortedSet<string>? s))
                list.AddRange(s);
            combined[key] = list;
        }

        return combined.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Projects the <c>ambiguous</c> maps from already-parsed entries as extension -&gt; alt languages.</summary>
    private static IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> EnumerateAmbiguous(
        Dictionary<string, ManifestEntry> raw)
    {
        foreach (ManifestEntry entry in raw.Values)
        {
            if (entry.Ambiguous is null)
                continue;
            foreach ((string ext, List<string> alts) in entry.Ambiguous)
                yield return new KeyValuePair<string, IReadOnlyList<string>>(ext, alts);
        }
    }

    private static Stream OpenManifest()
    {
        Assembly asm = typeof(Manifests).Assembly;
        return asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded manifest resource '{ResourceName}' was not found in {asm.GetName().Name}.");
    }
}

/// <summary>A raw manifest entry as it appears in <c>language_definitions.json</c>.</summary>
internal sealed class ManifestEntry
{
    [JsonPropertyName("repo")] public string? Repo { get; init; }
    [JsonPropertyName("rev")] public string? Rev { get; init; }
    [JsonPropertyName("branch")] public string? Branch { get; init; }
    [JsonPropertyName("directory")] public string? Directory { get; init; }
    [JsonPropertyName("generate")] public bool? Generate { get; init; }
    [JsonPropertyName("c_symbol")] public string? CSymbol { get; init; }
    [JsonPropertyName("extensions")] public List<string>? Extensions { get; init; }
    [JsonPropertyName("ambiguous")] public Dictionary<string, List<string>>? Ambiguous { get; init; }
    [JsonPropertyName("abi_version")] public int? AbiVersion { get; init; }
}

/// <summary>Source-generated JSON context for the manifest (trim/AOT-friendly).</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(Dictionary<string, ManifestEntry>))]
internal sealed partial class ManifestJsonContext : JsonSerializerContext;
