using TreeSitter.CodeGen.Model;
using TreeSitter.CodeGen.Schema;

namespace TreeSitter.Tests;

public class TypeRegistryTests
{
    private static TypeRegistry Build(string json, string root = "Test.Ns")
    {
        IReadOnlyList<NodeType> nodes = NodeTypesParser.Parse(json);
        return new TypeRegistry(root, nodes);
    }

    [Fact]
    public void Concrete_named_node_classified_in_root()
    {
        var reg = Build("""[ { "type": "identifier", "named": true } ]""");
        TypeEntry e = reg.Resolve("identifier", true);
        Assert.Equal(TypeCategory.Concrete, e.Category);
        Assert.Equal("Identifier", e.TypeName);
        Assert.Equal("Test.Ns.Identifier", e.FullName);
    }

    [Fact]
    public void Supertype_classified_as_supertype()
    {
        var reg = Build("""
        [
          { "type": "_value", "named": true, "subtypes": [ { "type": "number", "named": true } ] },
          { "type": "number", "named": true }
        ]
        """);
        TypeEntry e = reg.Resolve("_value", true);
        Assert.Equal(TypeCategory.Supertype, e.Category);
    }

    [Fact]
    public void Unnamed_alnum_token_goes_to_Unnamed_namespace()
    {
        var reg = Build("""[ { "type": "await", "named": false } ]""");
        TypeEntry e = reg.Resolve("await", false);
        Assert.Equal(TypeCategory.Unnamed, e.Category);
        Assert.Equal("Test.Ns.Unnamed.Await", e.FullName);
    }

    [Fact]
    public void Punctuation_token_goes_to_Symbols_namespace()
    {
        var reg = Build("""[ { "type": "+", "named": false } ]""");
        TypeEntry e = reg.Resolve("+", false);
        Assert.Equal(TypeCategory.Symbol, e.Category);
        Assert.Equal("Test.Ns.Symbols.Plus", e.FullName);
    }

    [Fact]
    public void Contains_and_resolve_by_typeref()
    {
        var reg = Build("""[ { "type": "identifier", "named": true } ]""");
        Assert.True(reg.Contains("identifier", true));
        Assert.False(reg.Contains("nope", true));
        TypeEntry e = reg.Resolve(new TypeRef("identifier", true));
        Assert.Equal("Identifier", e.TypeName);
    }

    [Fact]
    public void Key_falls_back_to_opposite_namedness()
    {
        // Only a named 'identifier' is declared; a reference with named:false resolves
        // to the named one via the twin fallback.
        var reg = Build("""[ { "type": "identifier", "named": true } ]""");
        TypeEntry e = reg.Resolve("identifier", false);
        Assert.Equal("Identifier", e.TypeName);
    }

    [Fact]
    public void Synthesized_reference_for_undeclared_type()
    {
        // 'hidden_rule' is referenced but never declared at top level; the registry
        // synthesizes a leaf entry so the reference resolves.
        var reg = Build("""
        [
          {
            "type": "x", "named": true,
            "fields": { "f": { "types": [ { "type": "hidden_rule", "named": true } ] } }
          }
        ]
        """);
        Assert.True(reg.Contains("hidden_rule", true));
        Assert.Equal("HiddenRule", reg.Resolve("hidden_rule", true).TypeName);
    }

    [Fact]
    public void FlattenToLeafKinds_concrete_returns_self()
    {
        var reg = Build("""[ { "type": "identifier", "named": true } ]""");
        Assert.Equal(["identifier"], reg.FlattenToLeafKinds("identifier", true));
    }

    [Fact]
    public void FlattenToLeafKinds_supertype_flattens_transitively()
    {
        var reg = Build("""
        [
          { "type": "_a", "named": true, "subtypes": [ { "type": "_b", "named": true }, { "type": "x", "named": true } ] },
          { "type": "_b", "named": true, "subtypes": [ { "type": "y", "named": true }, { "type": "z", "named": true } ] },
          { "type": "x", "named": true },
          { "type": "y", "named": true },
          { "type": "z", "named": true }
        ]
        """);
        IReadOnlyList<string> leaves = reg.FlattenToLeafKinds("_a", true);
        Assert.Equal(["x", "y", "z"], leaves); // ordinal-sorted leaf kinds
    }

    [Fact]
    public void FlattenToLeafKinds_handles_cycles()
    {
        // A pathological self-referential supertype must terminate.
        var reg = Build("""
        [
          { "type": "_a", "named": true, "subtypes": [ { "type": "_a", "named": true }, { "type": "x", "named": true } ] },
          { "type": "x", "named": true }
        ]
        """);
        Assert.Equal(["x"], reg.FlattenToLeafKinds("_a", true));
    }

    [Fact]
    public void ResolveMemberType_single_type_no_union()
    {
        var reg = Build("""[ { "type": "identifier", "named": true } ]""");
        ResolvedMemberType r = reg.ResolveMemberType([new TypeRef("identifier", true)]);
        Assert.Null(r.Union);
        Assert.Equal("Test.Ns.Identifier", r.FullName);
    }

    [Fact]
    public void ResolveMemberType_empty_is_untyped_node()
    {
        var reg = Build("""[ { "type": "identifier", "named": true } ]""");
        ResolvedMemberType r = reg.ResolveMemberType([]);
        Assert.Null(r.Union);
        Assert.Equal("global::TreeSitter.Typed.UntypedNode", r.FullName);
    }

    [Fact]
    public void ResolveMemberType_multi_type_creates_union_and_dedups()
    {
        var reg = Build("""
        [
          { "type": "a", "named": true },
          { "type": "b", "named": true }
        ]
        """);
        ResolvedMemberType first = reg.ResolveMemberType([new TypeRef("a", true), new TypeRef("b", true)]);
        Assert.NotNull(first.Union);
        Assert.Equal("A_B", first.Union!.TypeName);

        // Same member set in a different order resolves to the SAME union (dedup).
        ResolvedMemberType second = reg.ResolveMemberType([new TypeRef("b", true), new TypeRef("a", true)]);
        Assert.NotNull(second.Union);
        Assert.Same(first.Union, second.Union);
        Assert.Single(reg.AnonUnions);
    }

    [Fact]
    public void ResolveMemberType_long_joined_name_is_hashed()
    {
        // Many members whose joined name exceeds 100 chars -> hashed "AnonU..." name.
        var nodes = new List<string>();
        var refs = new List<TypeRef>();
        for (int i = 0; i < 12; i++)
        {
            string t = $"a_very_long_type_name_number_{i}";
            nodes.Add($$"""{ "type": "{{t}}", "named": true }""");
            refs.Add(new TypeRef(t, true));
        }
        var reg = Build("[" + string.Join(",", nodes) + "]");
        ResolvedMemberType r = reg.ResolveMemberType(refs);
        Assert.NotNull(r.Union);
        Assert.StartsWith("AnonU", r.Union!.TypeName);
    }

    [Fact]
    public void Entries_and_NodeFor_round_trip()
    {
        var reg = Build("""
        [
          { "type": "b", "named": true },
          { "type": "a", "named": true }
        ]
        """);
        IReadOnlyList<TypeEntry> entries = reg.Entries;
        // Deterministic ordinal-by-sexp ordering.
        Assert.Equal("a", entries[0].SExpression);
        Assert.Equal("b", entries[1].SExpression);
        NodeType nt = reg.NodeFor(entries[0]);
        Assert.Equal("a", nt.Type);
    }

    [Fact]
    public void Named_unnamed_twin_allocates_distinct_names()
    {
        // Python-style: 'type' exists as both a named rule and an unnamed token.
        var reg = Build("""
        [
          { "type": "type", "named": true },
          { "type": "type", "named": false }
        ]
        """);
        TypeEntry named = reg.Resolve("type", true);
        TypeEntry token = reg.Resolve("type", false);
        Assert.Equal(TypeCategory.Concrete, named.Category);
        Assert.Equal(TypeCategory.Unnamed, token.Category);
        Assert.NotEqual(named.FullName, token.FullName);
    }
}
