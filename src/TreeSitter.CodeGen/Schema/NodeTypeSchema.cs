using System.Text.Json;

namespace TreeSitter.CodeGen.Schema;

/// <summary>A reference to a node type from a field/children/subtype <c>types</c> list.</summary>
/// <param name="Type">The s-expression node type (e.g. <c>identifier</c>, <c>+</c>).</param>
/// <param name="Named">Whether the referenced type is a named rule.</param>
public readonly record struct TypeRef(string Type, bool Named);

/// <summary>A field or the unnamed <c>children</c> group of a node.</summary>
/// <param name="Multiple">Whether more than one matching child may appear.</param>
/// <param name="Required">Whether at least one matching child must appear.</param>
/// <param name="Types">The set of node types that may appear here.</param>
public sealed record ChildSet(bool Multiple, bool Required, IReadOnlyList<TypeRef> Types);

/// <summary>One entry in a grammar's <c>node-types.json</c>.</summary>
public sealed class NodeType
{
    /// <summary>The node's s-expression type string.</summary>
    public required string Type { get; init; }

    /// <summary>Whether the node is a named grammar rule.</summary>
    public required bool Named { get; init; }

    /// <summary>The node's named fields (empty when none), keyed by field name.</summary>
    public IReadOnlyDictionary<string, ChildSet> Fields { get; init; } =
        new Dictionary<string, ChildSet>();

    /// <summary>The unnamed <c>children</c> group, or <see langword="null"/> when absent.</summary>
    public ChildSet? Children { get; init; }

    /// <summary>The direct subtypes when this is a supertype node, else <see langword="null"/>.</summary>
    public IReadOnlyList<TypeRef>? Subtypes { get; init; }

    /// <summary>Whether this entry is a supertype (has <c>subtypes</c>).</summary>
    public bool IsSupertype => Subtypes is not null;
}

/// <summary>Parses <c>node-types.json</c> text into a <see cref="NodeType"/> model.</summary>
public static class NodeTypesParser
{
    private static readonly JsonDocumentOptions DocOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Parses the given <c>node-types.json</c> text.</summary>
    /// <param name="json">The raw JSON array text.</param>
    /// <returns>The parsed node-type entries, in document order.</returns>
    /// <exception cref="FormatException">The JSON is not a node-types array.</exception>
    public static IReadOnlyList<NodeType> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using JsonDocument doc = JsonDocument.Parse(json, DocOptions);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array)
            throw new FormatException("node-types.json must be a JSON array at the top level.");

        var result = new List<NodeType>(root.GetArrayLength());
        foreach (JsonElement entry in root.EnumerateArray())
            result.Add(ParseEntry(entry));
        return result;
    }

    private static NodeType ParseEntry(JsonElement entry)
    {
        string type = entry.GetProperty("type").GetString()
            ?? throw new FormatException("A node-type entry is missing its 'type'.");
        bool named = entry.TryGetProperty("named", out JsonElement n) && n.GetBoolean();

        IReadOnlyList<TypeRef>? subtypes = null;
        if (entry.TryGetProperty("subtypes", out JsonElement st))
            subtypes = ParseTypeRefs(st);

        var fields = new Dictionary<string, ChildSet>(StringComparer.Ordinal);
        if (entry.TryGetProperty("fields", out JsonElement fieldsEl) &&
            fieldsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty field in fieldsEl.EnumerateObject())
                fields[field.Name] = ParseChildSet(field.Value);
        }

        ChildSet? children = null;
        if (entry.TryGetProperty("children", out JsonElement childrenEl) &&
            childrenEl.ValueKind == JsonValueKind.Object)
        {
            children = ParseChildSet(childrenEl);
        }

        return new NodeType
        {
            Type = type,
            Named = named,
            Fields = fields,
            Children = children,
            Subtypes = subtypes,
        };
    }

    private static ChildSet ParseChildSet(JsonElement el)
    {
        bool multiple = el.TryGetProperty("multiple", out JsonElement m) && m.GetBoolean();
        bool required = el.TryGetProperty("required", out JsonElement r) && r.GetBoolean();
        IReadOnlyList<TypeRef> types = el.TryGetProperty("types", out JsonElement t)
            ? ParseTypeRefs(t)
            : [];
        return new ChildSet(multiple, required, types);
    }

    private static IReadOnlyList<TypeRef> ParseTypeRefs(JsonElement arr)
    {
        if (arr.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<TypeRef>(arr.GetArrayLength());
        foreach (JsonElement el in arr.EnumerateArray())
        {
            string type = el.GetProperty("type").GetString()
                ?? throw new FormatException("A type reference is missing its 'type'.");
            bool named = el.TryGetProperty("named", out JsonElement n) && n.GetBoolean();
            list.Add(new TypeRef(type, named));
        }
        return list;
    }
}
