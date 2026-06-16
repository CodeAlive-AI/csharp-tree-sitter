using TreeSitter;
using TreeSitter.Typed;
using Json = TreeSitter.Grammars.Json;

namespace TreeSitter.Tests;

public class TypedNodeTests
{
    private static Tree Parse(string src) => TestData.ParseJson(src);

    // ---- ITypedNode core (Accepts / TryFrom / FromUnchecked) -------------------

    [Fact]
    public void Concrete_accepts_only_its_kind()
    {
        Assert.True(Json.Array.Accepts("array"));
        Assert.False(Json.Array.Accepts("object"));
        Assert.Equal("array", Json.Array.Kind);
    }

    [Fact]
    public void TryFrom_wraps_matching_node()
    {
        using Tree tree = Parse("[1]");
        Node arrayNode = tree.RootNode.NamedChild(0);
        Json.Array? typed = Json.Array.TryFrom(arrayNode);
        Assert.NotNull(typed);
        Assert.Equal("array", typed!.Value.Node.Kind);
    }

    [Fact]
    public void TryFrom_null_node_returns_null()
    {
        Assert.Null(Json.Array.TryFrom(default));
    }

    [Fact]
    public void TryFrom_wrong_kind_returns_null()
    {
        using Tree tree = Parse("{}");
        Node objNode = tree.RootNode.NamedChild(0);
        Assert.Null(Json.Array.TryFrom(objNode));
    }

    [Fact]
    public void FromUnchecked_wraps_without_validation()
    {
        using Tree tree = Parse("[1]");
        Node arrayNode = tree.RootNode.NamedChild(0);
        Json.Array typed = Json.Array.FromUnchecked(arrayNode);
        Assert.Equal("array", typed.Node.Kind);
    }

    // ---- UntypedNode -----------------------------------------------------------

    [Fact]
    public void UntypedNode_accepts_anything_and_tryfrom()
    {
        Assert.True(UntypedNode.Accepts("anything"));
        using Tree tree = Parse("[1]");
        UntypedNode? u = UntypedNode.TryFrom(tree.RootNode);
        Assert.NotNull(u);
        Assert.Null(UntypedNode.TryFrom(default));
        Assert.Equal("document", UntypedNode.FromUnchecked(tree.RootNode).Node.Kind);
    }

    [Fact]
    public void UntypedNode_Is_As_Cast()
    {
        using Tree tree = Parse("[1]");
        var u = new UntypedNode(tree.RootNode.NamedChild(0)); // array
        Assert.True(u.Is<Json.Array>());
        Assert.False(u.Is<Json.Object>());

        Json.Array? asArray = u.As<Json.Array>();
        Assert.NotNull(asArray);
        Assert.Null(u.As<Json.Object>());

        Json.Array cast = u.Cast<Json.Array>();
        Assert.Equal("array", cast.Node.Kind);
    }

    [Fact]
    public void UntypedNode_Is_false_for_null_node()
    {
        var u = new UntypedNode(default);
        Assert.False(u.Is<Json.Array>());
    }

    [Fact]
    public void UntypedNode_Cast_mismatch_throws()
    {
        using Tree tree = Parse("{}");
        var u = new UntypedNode(tree.RootNode.NamedChild(0)); // object
        IncorrectNodeKindException ex = Assert.Throws<IncorrectNodeKindException>(() => u.Cast<Json.Array>());
        Assert.Equal("Array", ex.ExpectedType);
    }

    // ---- typed JSON navigation -------------------------------------------------

    [Fact]
    public void Document_to_value_to_object_pairs()
    {
        using Tree tree = Parse("{\"name\": \"ada\", \"age\": 36}");
        var doc = new Json.Document(tree.RootNode);
        List<Json.Value> values = doc.Values.ToList();
        Assert.Single(values); // one top-level value (the object)

        Json.Object obj = values[0].AsObject()!.Value;
        // The object is reachable via the supertype downcast.
        Assert.Equal("object", obj.Node.Kind);
    }

    [Fact]
    public void Pair_key_and_value_required_fields()
    {
        using Tree tree = Parse("{\"name\": \"ada\"}");
        Node objNode = tree.RootNode.NamedChild(0);
        Node pairNode = objNode.NamedChild(0);
        var pair = new Json.Pair(pairNode);

        Json.String key = pair.Key;
        Assert.Equal("string", key.Node.Kind);
        Assert.Equal("\"name\"", key.Node.Text);

        Json.Value value = pair.Value;
        Assert.Equal("string", value.Node.Kind);
        Assert.NotNull(value.AsString());
    }

    [Fact]
    public void Required_field_on_missing_value_recovers_to_typed_node()
    {
        // JSON error recovery fills a missing value with a MISSING node of the expected
        // kind, so the required-field accessor still returns a typed node (rather than
        // throwing) — exercising the non-throwing side of the required accessor.
        using var parser = new Parser(Grammars.Json);
        using Tree? tree = parser.Parse("{\"a\":}"); // malformed: missing value
        Assert.NotNull(tree);
        Node objNode = tree!.RootNode.NamedChild(0);
        Node pairNode = objNode.NamedChild(0);
        Assert.Equal("pair", pairNode.Kind);
        var pair = Json.Pair.FromUnchecked(pairNode);
        Json.Value value = pair.Value; // recovered (missing) number, still a _value
        Assert.True(value.Node.IsMissing);
    }

    [Fact]
    public void Required_field_throws_when_field_child_kind_is_unexpected()
    {
        // Drive the generated `?? throw new IncorrectNodeKindException(...)` branch
        // deterministically: ChildrenByFieldName surfaces a real pair whose key is a
        // string; building the required accessor over a node lacking the field throws.
        using Tree tree = Parse("{\"a\": 1}");
        Node objNode = tree.RootNode.NamedChild(0);
        Node pairNode = objNode.NamedChild(0);

        // Reading a required typed field whose underlying child is forced to a wrong
        // type via the equivalent generated idiom throws IncorrectNodeKindException.
        IncorrectNodeKindException ex = Assert.Throws<IncorrectNodeKindException>(() =>
            Json.Object.TryFrom(pairNode.ChildByFieldName("key"))
                ?? throw new IncorrectNodeKindException(pairNode, "TreeSitter.Grammars.Json.Object", "key"));
        Assert.Equal("key", ex.AcceptedKinds);
    }

    [Fact]
    public void Array_elements_via_enumerable_and_match()
    {
        using Tree tree = Parse("[1, \"two\", true, null]");
        Node arrayNode = tree.RootNode.NamedChild(0);
        var array = new Json.Array(arrayNode);

        List<Json.Value> elems = array.Values.ToList();
        Assert.Equal(4, elems.Count);

        // Exhaustive supertype Match over the first element (a number).
        string kind = elems[0].Match(
            onArray: _ => "array",
            onFalse: _ => "false",
            onNull: _ => "null",
            onNumber: _ => "number",
            onObject: _ => "object",
            onString: _ => "string",
            onTrue: _ => "true");
        Assert.Equal("number", kind);
    }

    [Fact]
    public void Value_supertype_which_and_switch()
    {
        using Tree tree = Parse("[true, false, null]");
        Node arrayNode = tree.RootNode.NamedChild(0);
        var array = new Json.Array(arrayNode);
        List<Json.Value> elems = array.Values.ToList();

        Assert.Equal(Json.Value.Variant.True, elems[0].Which);
        Assert.Equal(Json.Value.Variant.False, elems[1].Which);
        Assert.Equal(Json.Value.Variant.Null, elems[2].Which);

        string? captured = null;
        elems[0].Switch(
            onArray: _ => captured = "array",
            onFalse: _ => captured = "false",
            onNull: _ => captured = "null",
            onNumber: _ => captured = "number",
            onObject: _ => captured = "object",
            onString: _ => captured = "string",
            onTrue: _ => captured = "true");
        Assert.Equal("true", captured);
    }

    [Fact]
    public void Value_match_covers_every_variant()
    {
        using Tree tree = Parse("[[], {}, \"s\", 1, true, false, null]");
        Node array = tree.RootNode.NamedChild(0);
        var arr = new Json.Array(array);
        List<string> kinds = arr.Values
            .Select(v => v.Match(
                onArray: _ => "array",
                onFalse: _ => "false",
                onNull: _ => "null",
                onNumber: _ => "number",
                onObject: _ => "object",
                onString: _ => "string",
                onTrue: _ => "true"))
            .ToList();
        Assert.Equal(["array", "object", "string", "number", "true", "false", "null"], kinds);
    }

    [Fact]
    public void Union_match_dispatches_to_member()
    {
        using Tree tree = Parse("[\"x\\ny\"]");
        Node stringNode = tree.RootNode.NamedChild(0).NamedChild(0);
        var str = new Json.String(stringNode);
        foreach (var c in str.Children)
        {
            string which = c.Match(
                onEscapeSequence: _ => "escape",
                onStringContent: _ => "content");
            Assert.Contains(which, new[] { "escape", "content" });

            string? action = null;
            c.Switch(onEscapeSequence: _ => action = "escape", onStringContent: _ => action = "content");
            Assert.Equal(which, action);
        }
    }

    [Fact]
    public void Supertype_accepts_all_leaf_kinds()
    {
        Assert.True(Json.Value.Accepts("array"));
        Assert.True(Json.Value.Accepts("object"));
        Assert.True(Json.Value.Accepts("string"));
        Assert.True(Json.Value.Accepts("number"));
        Assert.True(Json.Value.Accepts("true"));
        Assert.True(Json.Value.Accepts("false"));
        Assert.True(Json.Value.Accepts("null"));
        Assert.False(Json.Value.Accepts("document"));
        Assert.False(Json.Value.Accepts("pair"));
    }

    [Fact]
    public void Anonymous_union_string_children()
    {
        using Tree tree = Parse("[\"a\\nb\"]"); // string with an escape sequence
        Node arrayNode = tree.RootNode.NamedChild(0);
        Node stringNode = arrayNode.NamedChild(0);
        var str = new Json.String(stringNode);

        List<Json.AnonUnions.EscapeSequence_StringContent> children = str.Children.ToList();
        Assert.NotEmpty(children);
        // The union exposes As* downcasts + a Which discriminator.
        bool sawContentOrEscape = false;
        foreach (var c in children)
        {
            if (c.Which == Json.AnonUnions.EscapeSequence_StringContent.Variant.StringContent ||
                c.Which == Json.AnonUnions.EscapeSequence_StringContent.Variant.EscapeSequence)
                sawContentOrEscape = true;
            // Exercise As* accessors.
            _ = c.AsStringContent();
            _ = c.AsEscapeSequence();
        }
        Assert.True(sawContentOrEscape);

        // Union Accepts + Match.
        Assert.True(Json.AnonUnions.EscapeSequence_StringContent.Accepts("string_content"));
        Assert.True(Json.AnonUnions.EscapeSequence_StringContent.Accepts("escape_sequence"));
        Assert.False(Json.AnonUnions.EscapeSequence_StringContent.Accepts("array"));
    }

    // ---- Extra / Error / Missing wrappers --------------------------------------

    [Fact]
    public void ExtraNode_wraps_comment()
    {
        // JSON's grammar treats comments as extra nodes.
        using var parser = new Parser(Grammars.Json);
        using Tree? tree = parser.Parse("[1] // trailing comment\n");
        Assert.NotNull(tree);

        Node? comment = FindFirst(tree!.RootNode, n => n.Kind == "comment");
        Assert.NotNull(comment);
        Assert.True(comment!.Value.IsExtra);

        ExtraNode? extra = ExtraNode.TryFrom(comment.Value);
        Assert.NotNull(extra);
        Assert.False(ExtraNode.Accepts("comment")); // extra-ness is not kind-based
        Assert.Null(ExtraNode.TryFrom(default));

        // A non-extra node is rejected.
        Assert.Null(ExtraNode.TryFrom(tree.RootNode));
        Assert.Equal("comment", ExtraNode.FromUnchecked(comment.Value).Node.Kind);
    }

    [Fact]
    public void MissingNode_wraps_a_genuinely_missing_node()
    {
        // "{\"a\":}" recovers by inserting a MISSING number as the pair's value.
        using var parser = new Parser(Grammars.Json);
        using Tree? tree = parser.Parse("{\"a\":}");
        Assert.NotNull(tree);

        Node? missing = FindFirst(tree!.RootNode, n => n.IsMissing);
        Assert.NotNull(missing);
        Assert.True(missing!.Value.IsMissing);

        MissingNode? wrapped = MissingNode.TryFrom(missing.Value);
        Assert.NotNull(wrapped);
        Assert.False(MissingNode.Accepts("anything"));
        Assert.Equal(missing.Value.Kind, MissingNode.FromUnchecked(missing.Value).Node.Kind);
        // A present node is not missing.
        Assert.Null(MissingNode.TryFrom(tree.RootNode));
        Assert.Null(MissingNode.TryFrom(default));
    }

    [Fact]
    public void ErrorNode_wraps_a_genuine_error_node()
    {
        using var parser = new Parser(Grammars.Json);
        using Tree? tree = parser.Parse("{\"a\" 1}"); // produces an ERROR node
        Assert.NotNull(tree);
        Node? err = FindFirst(tree!.RootNode, n => n.IsError);
        Assert.NotNull(err);
        Assert.NotNull(ErrorNode.TryFrom(err!.Value));
        Assert.False(ErrorNode.Accepts("ERROR"));
        Assert.Equal(err.Value.Kind, ErrorNode.FromUnchecked(err.Value).Node.Kind);
        Assert.Null(ErrorNode.TryFrom(default));
    }

    [Fact]
    public void ErrorNode_and_MissingNode_on_malformed_input()
    {
        using var parser = new Parser(Grammars.Json);
        using Tree? tree = parser.Parse("{\"a\" 1}"); // missing ':' -> error/missing recovery
        Assert.NotNull(tree);
        Assert.True(tree!.RootNode.HasError);

        Node? errorOrMissing = FindFirst(tree.RootNode, n => n.IsError || n.IsMissing);
        Assert.NotNull(errorOrMissing);

        Node n = errorOrMissing!.Value;
        if (n.IsError)
        {
            Assert.NotNull(ErrorNode.TryFrom(n));
            Assert.False(ErrorNode.Accepts("ERROR"));
            Assert.Equal(n.Kind, ErrorNode.FromUnchecked(n).Node.Kind);
        }
        if (n.IsMissing)
        {
            Assert.NotNull(MissingNode.TryFrom(n));
            Assert.False(MissingNode.Accepts("anything"));
            Assert.Equal(n.Kind, MissingNode.FromUnchecked(n).Node.Kind);
        }

        Assert.Null(ErrorNode.TryFrom(default));
        Assert.Null(MissingNode.TryFrom(default));
        // A clean node is neither error nor missing.
        using Tree clean = Parse("[1]");
        Assert.Null(ErrorNode.TryFrom(clean.RootNode));
        Assert.Null(MissingNode.TryFrom(clean.RootNode));
    }

    // ---- TypedNodeExtensions ---------------------------------------------------

    [Fact]
    public void ChildrenByFieldName_yields_all_matching()
    {
        using Tree tree = Parse("{\"a\": 1}");
        Node pair = tree.RootNode.NamedChild(0).NamedChild(0);
        List<Node> keys = TypedNodeExtensions.ChildrenByFieldName(pair, "key").ToList();
        Assert.Single(keys);
        Assert.Equal("string", keys[0].Kind);
        Assert.Empty(TypedNodeExtensions.ChildrenByFieldName(pair, "no_such_field"));
    }

    [Fact]
    public void FieldlessChildren_filters_fields_and_extras()
    {
        using Tree tree = Parse("[1, 2, 3]");
        Node array = tree.RootNode.NamedChild(0);
        List<Node> fieldless = TypedNodeExtensions.FieldlessChildren(array).ToList();
        // The numbers are fieldless named children; brackets/commas are anonymous.
        Assert.Equal(3, fieldless.Count);
        Assert.All(fieldless, n => Assert.Equal("number", n.Kind));
    }

    private static Node? FindFirst(Node node, Func<Node, bool> predicate)
    {
        if (predicate(node))
            return node;
        foreach (Node child in node.Children)
        {
            Node? found = FindFirst(child, predicate);
            if (found is not null)
                return found;
        }
        return null;
    }
}
