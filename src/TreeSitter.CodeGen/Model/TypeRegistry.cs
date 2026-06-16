using System.Security.Cryptography;
using System.Text;
using TreeSitter.CodeGen.Naming;
using TreeSitter.CodeGen.Schema;

namespace TreeSitter.CodeGen.Model;

/// <summary>The sub-namespace a generated type lives in, relative to the root.</summary>
public enum TypeCategory
{
    /// <summary>A concrete named node (root namespace).</summary>
    Concrete,
    /// <summary>A supertype node (root namespace).</summary>
    Supertype,
    /// <summary>An unnamed token whose name has alphanumerics (<c>Unnamed</c> namespace).</summary>
    Unnamed,
    /// <summary>An unnamed token whose name is pure punctuation (<c>Symbols</c> namespace).</summary>
    Symbol,
    /// <summary>A generated anonymous union (<c>AnonUnions</c> namespace).</summary>
    AnonUnion,
}

/// <summary>
/// Uniquely identifies a node type. The s-expression string is not unique on its
/// own: a grammar may declare the same string as both a named rule and an unnamed
/// token (e.g. Python's <c>await</c>, <c>lambda</c>, <c>type</c>, <c>yield</c>), so
/// the <see cref="Named"/> flag is part of the identity.
/// </summary>
/// <param name="Type">The s-expression node type string.</param>
/// <param name="Named">Whether this is the named-rule symbol.</param>
public readonly record struct NodeKey(string Type, bool Named);

/// <summary>An entry describing one generated typed struct and how to refer to it.</summary>
public sealed class TypeEntry
{
    /// <summary>The original s-expression node type, or a synthetic key for unions.</summary>
    public required string SExpression { get; init; }

    /// <summary>Whether this entry corresponds to the named-rule symbol.</summary>
    public required bool Named { get; init; }

    /// <summary>The simple (unqualified) C# type name.</summary>
    public required string TypeName { get; init; }

    /// <summary>The category / sub-namespace this type belongs to.</summary>
    public required TypeCategory Category { get; init; }

    /// <summary>The fully-qualified type name (including the sub-namespace).</summary>
    public required string FullName { get; init; }
}

/// <summary>
/// Owns the mapping from grammar node types to generated C# type names, allocates
/// unique names per sub-namespace, flattens supertypes to their leaf concrete kinds,
/// and creates/deduplicates anonymous unions for multi-type fields.
/// </summary>
public sealed class TypeRegistry
{
    private readonly string _root;
    private readonly IReadOnlyDictionary<NodeKey, NodeType> _byKey;

    // Allocation scopes: one per sub-namespace so names are unique where they live.
    private readonly NameAllocator _rootScope = new();
    private readonly NameAllocator _unnamedScope = new();
    private readonly NameAllocator _symbolScope = new();
    private readonly NameAllocator _anonScope = new();

    // (type, named) -> resolved type entry (named/unnamed leaf and supertype nodes).
    private readonly Dictionary<NodeKey, TypeEntry> _entries = new();

    // Deduplicated anonymous unions keyed by their canonical member-set signature.
    private readonly Dictionary<string, AnonUnion> _unions = new(StringComparer.Ordinal);

    private const int MaxJoinedNameLength = 100;

    /// <summary>Builds a registry for the given node types under <paramref name="rootNamespace"/>.</summary>
    /// <param name="rootNamespace">The generated root namespace.</param>
    /// <param name="nodeTypes">All node-type entries from the grammar.</param>
    public TypeRegistry(string rootNamespace, IReadOnlyList<NodeType> nodeTypes)
    {
        _root = rootNamespace;
        IReadOnlyList<NodeType> all = WithSynthesizedReferences(nodeTypes);
        var byKey = new Dictionary<NodeKey, NodeType>();
        foreach (NodeType nt in all)
            byKey[new NodeKey(nt.Type, nt.Named)] = nt; // last write wins on exact-dup keys
        _byKey = byKey;
        AllocateAllTypes(all);
    }

    /// <summary>
    /// Some grammars reference a type from a field/children/subtype list that has no
    /// top-level entry of its own (a hidden rule, e.g. Python's
    /// <c>as_pattern_target</c>). To keep references resolvable we synthesize a
    /// minimal leaf entry (no fields/children) for each such reference.
    /// </summary>
    private static IReadOnlyList<NodeType> WithSynthesizedReferences(IReadOnlyList<NodeType> nodeTypes)
    {
        var declared = new HashSet<NodeKey>();
        foreach (NodeType nt in nodeTypes)
            declared.Add(new NodeKey(nt.Type, nt.Named));

        var synthetic = new Dictionary<NodeKey, NodeType>();
        void Consider(TypeRef r)
        {
            var key = new NodeKey(r.Type, r.Named);
            // Only synthesize when neither this exact key nor its named/unnamed twin exists.
            if (declared.Contains(key) ||
                declared.Contains(new NodeKey(r.Type, !r.Named)) ||
                synthetic.ContainsKey(key))
            {
                return;
            }
            synthetic[key] = new NodeType { Type = r.Type, Named = r.Named };
        }

        foreach (NodeType nt in nodeTypes)
        {
            foreach (ChildSet field in nt.Fields.Values)
                foreach (TypeRef r in field.Types)
                    Consider(r);
            if (nt.Children is { } children)
                foreach (TypeRef r in children.Types)
                    Consider(r);
            if (nt.Subtypes is { } subs)
                foreach (TypeRef r in subs)
                    Consider(r);
        }

        if (synthetic.Count == 0)
            return nodeTypes;

        var combined = new List<NodeType>(nodeTypes.Count + synthetic.Count);
        combined.AddRange(nodeTypes);
        combined.AddRange(synthetic.Values);
        return combined;
    }

    /// <summary>All non-union type entries, in deterministic order (by sexp then named).</summary>
    public IReadOnlyList<TypeEntry> Entries =>
        _entries.Values
            .OrderBy(e => e.SExpression, StringComparer.Ordinal)
            .ThenBy(e => e.Named)
            .ToList();

    /// <summary>All distinct anonymous unions, in deterministic name order.</summary>
    public IReadOnlyList<AnonUnion> AnonUnions =>
        _unions.Values.OrderBy(u => u.TypeName, StringComparer.Ordinal).ToList();

    /// <summary>Looks up the resolved entry for a node type reference.</summary>
    /// <param name="type">The node type string.</param>
    /// <param name="named">Whether the named-rule symbol is meant.</param>
    public TypeEntry Resolve(string type, bool named) => _entries[Key(type, named)];

    /// <summary>Looks up the resolved entry for a type reference.</summary>
    /// <param name="reference">A <see cref="TypeRef"/> from the schema.</param>
    public TypeEntry Resolve(TypeRef reference) => Resolve(reference.Type, reference.Named);

    /// <summary>Whether the given node type reference is known to the registry.</summary>
    /// <param name="type">The node type string.</param>
    /// <param name="named">Whether the named-rule symbol is meant.</param>
    public bool Contains(string type, bool named) => _entries.ContainsKey(Key(type, named));

    /// <summary>
    /// Returns the schema <see cref="NodeType"/> backing a resolved entry, including
    /// the synthetic leaf entries created for referenced-but-undeclared types.
    /// </summary>
    /// <param name="entry">A resolved type entry.</param>
    public NodeType NodeFor(TypeEntry entry) =>
        _byKey[new NodeKey(entry.SExpression, entry.Named)];

    // ---- name allocation -------------------------------------------------------

    private void AllocateAllTypes(IReadOnlyList<NodeType> nodeTypes)
    {
        // Deterministic: ordinal by s-expression, then named-rules before tokens so a
        // named/unnamed clash on the bare name resolves the same way every run.
        foreach (NodeType nt in nodeTypes
                     .OrderBy(n => n.Type, StringComparer.Ordinal)
                     .ThenByDescending(n => n.Named))
        {
            string baseName = Identifiers.ToTypeName(nt.Type);
            (TypeCategory category, NameAllocator scope, string? subNs) = Classify(nt);
            string allocated = scope.Allocate(baseName);
            _entries[new NodeKey(nt.Type, nt.Named)] = new TypeEntry
            {
                SExpression = nt.Type,
                Named = nt.Named,
                TypeName = allocated,
                Category = category,
                FullName = Qualify(subNs, allocated),
            };
        }
    }

    private (TypeCategory, NameAllocator, string? subNs) Classify(NodeType nt)
    {
        if (nt.IsSupertype)
            return (TypeCategory.Supertype, _rootScope, null);
        if (nt.Named)
            return (TypeCategory.Concrete, _rootScope, null);
        return Identifiers.ContainsPunctuation(nt.Type)
            ? (TypeCategory.Symbol, _symbolScope, "Symbols")
            : (TypeCategory.Unnamed, _unnamedScope, "Unnamed");
    }

    private string Qualify(string? subNs, string typeName) =>
        subNs is null ? $"{_root}.{typeName}" : $"{_root}.{subNs}.{typeName}";

    private NodeKey Key(string type, bool named)
    {
        var k = new NodeKey(type, named);
        if (_entries.ContainsKey(k))
            return k;
        // A reference's `named` flag occasionally disagrees with the only declared
        // symbol of that name (e.g. a supertype listing a token, or vice versa).
        // Fall back to the other named-ness when only one symbol exists.
        var alt = new NodeKey(type, !named);
        return _entries.ContainsKey(alt) ? alt : k;
    }

    // ---- supertype flattening --------------------------------------------------

    /// <summary>
    /// Returns the set of leaf concrete (and unnamed) kind <em>strings</em> reachable
    /// from a node type, following supertype subtype edges transitively. A concrete
    /// type returns its own kind. Sorted ordinally for deterministic emission. Note
    /// the result is over node <c>Kind</c> strings, which is exactly what an
    /// <c>Accepts</c> switch compares against.
    /// </summary>
    /// <param name="type">The starting node type.</param>
    /// <param name="named">Whether the named-rule symbol is meant.</param>
    public IReadOnlyList<string> FlattenToLeafKinds(string type, bool named)
    {
        var seen = new SortedSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<NodeKey>();
        Collect(type, named);
        return seen.ToList();

        void Collect(string t, bool n)
        {
            var key = new NodeKey(t, n);
            if (!visiting.Add(key))
                return; // guard against pathological cycles

            NodeType? nt = Lookup(t, n);
            if (nt is { IsSupertype: true, Subtypes: { } subs })
            {
                foreach (TypeRef sub in subs)
                    Collect(sub.Type, sub.Named);
            }
            else
            {
                seen.Add(t);
            }
            visiting.Remove(key);
        }
    }

    private NodeType? Lookup(string type, bool named)
    {
        if (_byKey.TryGetValue(new NodeKey(type, named), out NodeType? nt))
            return nt;
        return _byKey.TryGetValue(new NodeKey(type, !named), out nt) ? nt : null;
    }

    // ---- anonymous unions ------------------------------------------------------

    /// <summary>
    /// Resolves a set of member types into a single referenceable C# type: the lone
    /// member's type when there is exactly one, otherwise a (deduplicated) anonymous
    /// union. The returned full name can be used directly in generated code.
    /// </summary>
    /// <param name="types">The member type references from a field/children group.</param>
    /// <returns>The full type name and, when a union was created/found, its model.</returns>
    public ResolvedMemberType ResolveMemberType(IReadOnlyList<TypeRef> types)
    {
        // Distinct by resolved entry (so a named/unnamed pair collapsing to one symbol
        // doesn't produce a degenerate union), preserving deterministic order.
        var memberEntries = types
            .Select(Resolve)
            .GroupBy(e => e.FullName, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(e => e.FullName, StringComparer.Ordinal)
            .ToList();

        if (memberEntries.Count == 0)
            return new ResolvedMemberType("global::TreeSitter.Typed.UntypedNode", null);

        if (memberEntries.Count == 1)
            return new ResolvedMemberType(memberEntries[0].FullName, null);

        // Build a canonical signature for dedup keyed on member full names.
        string signature = string.Join("|", memberEntries.Select(e => e.FullName));
        if (_unions.TryGetValue(signature, out AnonUnion? existing))
            return new ResolvedMemberType(existing.FullName, existing);

        // Name = member type names joined by '_', hashed if too long.
        string joined = string.Join("_", memberEntries.Select(e => e.TypeName));
        string unionName = joined.Length <= MaxJoinedNameLength ? joined : HashName(signature);
        string allocated = _anonScope.Allocate(unionName);

        var union = new AnonUnion
        {
            TypeName = allocated,
            FullName = $"{_root}.AnonUnions.{allocated}",
            Members = memberEntries,
            Signature = signature,
        };
        _unions[signature] = union;
        return new ResolvedMemberType(union.FullName, union);
    }

    private static string HashName(string signature)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(signature));
        var sb = new StringBuilder("AnonU", 21);
        for (int i = 0; i < 8; i++) // 8 bytes -> 16 hex chars
            sb.Append(hash[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}

/// <summary>The outcome of resolving a field/children member type.</summary>
/// <param name="FullName">The fully-qualified C# type to use in accessors.</param>
/// <param name="Union">The union model when one was created/reused, else <see langword="null"/>.</param>
public readonly record struct ResolvedMemberType(string FullName, AnonUnion? Union);

/// <summary>A generated anonymous-union type over two or more member types.</summary>
public sealed class AnonUnion
{
    /// <summary>The simple C# type name.</summary>
    public required string TypeName { get; init; }

    /// <summary>The fully-qualified name (in the <c>AnonUnions</c> namespace).</summary>
    public required string FullName { get; init; }

    /// <summary>The member type entries (distinct, ordinal-sorted).</summary>
    public required IReadOnlyList<TypeEntry> Members { get; init; }

    /// <summary>The canonical signature used for deduplication.</summary>
    public required string Signature { get; init; }
}
