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
    /// Reads the embedded manifest and projects it to a frozen
    /// <c>name -&gt; LanguageInfo</c> dictionary.
    /// </summary>
    internal static FrozenDictionary<string, LanguageInfo> Load()
    {
        using Stream stream = OpenManifest();
        Dictionary<string, ManifestEntry>? raw =
            JsonSerializer.Deserialize(stream, ManifestJsonContext.Default.DictionaryStringManifestEntry);

        if (raw is null || raw.Count == 0)
            throw new InvalidOperationException("The embedded language manifest is empty or could not be parsed.");

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

        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>The default ABI version when the manifest does not specify one.</summary>
    internal const int DefaultAbiVersion = 14;

    /// <summary>
    /// Builds the extension index: each extension (lowercase, no dot) maps to the
    /// languages that claim it, primary owners first then ordinally by name.
    /// </summary>
    internal static FrozenDictionary<string, IReadOnlyList<string>> BuildExtensionIndex(
        IReadOnlyDictionary<string, LanguageInfo> manifest) =>
        BuildExtensionIndexCore(manifest, LoadAmbiguous());

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

    /// <summary>Reads the <c>ambiguous</c> maps from the raw manifest as extension -&gt; alt languages.</summary>
    private static IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> LoadAmbiguous()
    {
        using Stream stream = OpenManifest();
        Dictionary<string, ManifestEntry>? raw =
            JsonSerializer.Deserialize(stream, ManifestJsonContext.Default.DictionaryStringManifestEntry);
        if (raw is null)
            yield break;

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
