using TreeSitter.CodeGen.Schema;

namespace TreeSitter.Tests;

public class SchemaTests
{
    [Fact]
    public void Parse_regular_node_with_fields_and_children()
    {
        const string json = """
        [
          {
            "type": "binary_expression",
            "named": true,
            "fields": {
              "left": { "multiple": false, "required": true, "types": [{ "type": "expression", "named": true }] },
              "right": { "multiple": false, "required": false, "types": [{ "type": "expression", "named": true }] }
            },
            "children": {
              "multiple": true, "required": false,
              "types": [{ "type": "comment", "named": true }]
            }
          }
        ]
        """;
        IReadOnlyList<NodeType> nodes = NodeTypesParser.Parse(json);
        NodeType bin = Assert.Single(nodes);
        Assert.Equal("binary_expression", bin.Type);
        Assert.True(bin.Named);
        Assert.False(bin.IsSupertype);

        Assert.Equal(2, bin.Fields.Count);
        ChildSet left = bin.Fields["left"];
        Assert.True(left.Required);
        Assert.False(left.Multiple);
        Assert.Equal("expression", left.Types[0].Type);
        Assert.True(left.Types[0].Named);

        ChildSet right = bin.Fields["right"];
        Assert.False(right.Required);

        Assert.NotNull(bin.Children);
        Assert.True(bin.Children!.Multiple);
        Assert.Equal("comment", bin.Children.Types[0].Type);
    }

    [Fact]
    public void Parse_supertype_node()
    {
        const string json = """
        [
          {
            "type": "_expression",
            "named": true,
            "subtypes": [
              { "type": "binary_expression", "named": true },
              { "type": "identifier", "named": true }
            ]
          }
        ]
        """;
        NodeType sup = Assert.Single(NodeTypesParser.Parse(json));
        Assert.True(sup.IsSupertype);
        Assert.NotNull(sup.Subtypes);
        Assert.Equal(2, sup.Subtypes!.Count);
        Assert.Equal("binary_expression", sup.Subtypes[0].Type);
    }

    [Fact]
    public void Parse_unnamed_token()
    {
        const string json = """[ { "type": "+", "named": false } ]""";
        NodeType tok = Assert.Single(NodeTypesParser.Parse(json));
        Assert.Equal("+", tok.Type);
        Assert.False(tok.Named);
        Assert.Empty(tok.Fields);
        Assert.Null(tok.Children);
        Assert.Null(tok.Subtypes);
    }

    [Fact]
    public void Parse_node_missing_named_defaults_false()
    {
        const string json = """[ { "type": "x" } ]""";
        NodeType n = Assert.Single(NodeTypesParser.Parse(json));
        Assert.False(n.Named);
    }

    [Fact]
    public void Parse_allows_trailing_commas_and_comments()
    {
        const string json = """
        [
          // a comment
          { "type": "a", "named": true, },
        ]
        """;
        Assert.Single(NodeTypesParser.Parse(json));
    }

    [Fact]
    public void Parse_field_without_types_yields_empty_types()
    {
        const string json = """
        [ { "type": "x", "named": true, "fields": { "f": { "required": false, "multiple": false } } } ]
        """;
        NodeType n = Assert.Single(NodeTypesParser.Parse(json));
        Assert.Empty(n.Fields["f"].Types);
    }

    [Fact]
    public void Parse_null_throws()
    {
        Assert.Throws<ArgumentNullException>(() => NodeTypesParser.Parse(null!));
    }

    [Fact]
    public void Parse_non_array_root_throws()
    {
        Assert.Throws<FormatException>(() => NodeTypesParser.Parse("""{ "type": "x" }"""));
    }

    [Fact]
    public void Parse_entry_missing_type_throws()
    {
        // An absent "type" property throws KeyNotFoundException from GetProperty.
        Assert.Throws<KeyNotFoundException>(() => NodeTypesParser.Parse("""[ { "named": true } ]"""));
    }

    [Fact]
    public void Parse_entry_null_type_throws_format()
    {
        // A JSON null "type" reaches the explicit FormatException guard.
        Assert.Throws<FormatException>(() => NodeTypesParser.Parse("""[ { "type": null } ]"""));
    }

    [Fact]
    public void Parse_typeref_missing_type_throws()
    {
        const string json = """
        [ { "type": "x", "named": true, "fields": { "f": { "types": [ { "named": true } ] } } } ]
        """;
        Assert.Throws<KeyNotFoundException>(() => NodeTypesParser.Parse(json));
    }

    [Fact]
    public void Parse_typeref_null_type_throws_format()
    {
        const string json = """
        [ { "type": "x", "named": true, "fields": { "f": { "types": [ { "type": null } ] } } } ]
        """;
        Assert.Throws<FormatException>(() => NodeTypesParser.Parse(json));
    }

    [Fact]
    public void Parse_subtypes_non_array_yields_empty()
    {
        // A 'subtypes' that is not an array degrades to an empty type-ref list, but the
        // presence of the property still marks the node as a supertype.
        const string json = """[ { "type": "_x", "named": true, "subtypes": {} } ]""";
        NodeType n = Assert.Single(NodeTypesParser.Parse(json));
        Assert.True(n.IsSupertype);
        Assert.Empty(n.Subtypes!);
    }

    [Fact]
    public void TypeRef_and_ChildSet_records()
    {
        var r = new TypeRef("identifier", true);
        Assert.Equal("identifier", r.Type);
        Assert.True(r.Named);
        Assert.Equal(new TypeRef("identifier", true), r);

        var cs = new ChildSet(true, false, [r]);
        Assert.True(cs.Multiple);
        Assert.False(cs.Required);
        Assert.Single(cs.Types);
    }

    [Fact]
    public void Parse_real_json_grammar_source()
    {
        // Use the vendored node-types.json so this test is hermetic (no /tmp dependency).
        string json = TestData.VendoredNodeTypesJson("json");
        IReadOnlyList<NodeType> nodes = NodeTypesParser.Parse(json);
        Assert.NotEmpty(nodes);
        // The _value supertype is present.
        Assert.Contains(nodes, n => n.Type == "_value" && n.IsSupertype);
        // The pair node has key and value fields.
        NodeType pair = nodes.First(n => n.Type == "pair");
        Assert.Contains("key", pair.Fields.Keys);
        Assert.Contains("value", pair.Fields.Keys);
    }
}
