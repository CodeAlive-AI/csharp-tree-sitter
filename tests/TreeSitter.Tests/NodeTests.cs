using System.Text;
using TreeSitter;

namespace TreeSitter.Tests;

public class NodeTests
{
    private static Tree Tree(string src) => TestData.ParseJson(src);

    [Fact]
    public void Kind_KindId_and_grammar_properties()
    {
        using Tree tree = Tree("{\"a\": 1}");
        Node root = tree.RootNode;
        Assert.Equal("document", root.Kind);
        Assert.True(root.KindId > 0);
        Assert.Equal("document", root.GrammarKind);
        Assert.True(root.GrammarSymbol > 0);
    }

    [Fact]
    public void Named_missing_extra_error_flags()
    {
        using Tree tree = Tree("{\"a\": 1}");
        Node root = tree.RootNode;
        Assert.True(root.IsNamed);
        Assert.False(root.IsMissing);
        Assert.False(root.IsExtra);
        Assert.False(root.IsError);
        Assert.False(root.HasError);
        Assert.False(root.HasChanges);
    }

    [Fact]
    public void HasError_true_for_malformed()
    {
        using var parser = new Parser(Grammars.Json);
        using Tree? tree = parser.Parse("{\"a\": }");
        Assert.NotNull(tree);
        Assert.True(tree!.RootNode.HasError);
    }

    [Fact]
    public void HasChanges_true_after_edit()
    {
        using Tree tree = Tree("[1]");
        tree.Edit(new InputEdit(0, 1, 2, new Point(0, 0), new Point(0, 1), new Point(0, 2)));
        Assert.True(tree.RootNode.HasChanges);
    }

    [Fact]
    public void ParseState_properties()
    {
        using Tree tree = Tree("[1]");
        Node root = tree.RootNode;
        // These are simply queried; assert they are callable and consistent typed.
        _ = root.ParseState;
        _ = root.NextParseState;
        Assert.True(root.NextParseState >= 0);
    }

    [Fact]
    public void Byte_and_point_offsets_and_range()
    {
        using Tree tree = Tree("[1, 2]");
        Node root = tree.RootNode;
        Assert.Equal(0u, root.StartByte);
        Assert.Equal(6u, root.EndByte);
        Assert.Equal(new Point(0, 0), root.StartPoint);
        Assert.Equal(new Point(0, 6), root.EndPoint);

        Range range = root.Range;
        Assert.Equal(0u, range.StartByte);
        Assert.Equal(6u, range.EndByte);
        Assert.Equal(6u, range.ByteLength);
    }

    [Fact]
    public void DescendantCount_and_child_counts()
    {
        using Tree tree = Tree("[1, 2]");
        Node root = tree.RootNode;
        Assert.True(root.DescendantCount > 1);
        Assert.True(root.ChildCount >= 1);
        Assert.True(root.NamedChildCount >= 1);
    }

    [Fact]
    public void Parent_child_and_navigation()
    {
        using Tree tree = Tree("[1, 2]");
        Node root = tree.RootNode;
        Node array = root.NamedChild(0);
        Assert.Equal("array", array.Kind);
        Assert.True(array.Parent.Equals(root));

        Node firstChild = array.Child(0); // "["
        Assert.False(firstChild.IsNull);
        Assert.Equal("[", firstChild.Kind);

        // Out-of-range child is a null node.
        Assert.True(array.Child(9999).IsNull);
        Assert.True(array.NamedChild(9999).IsNull);
    }

    [Fact]
    public void ChildWithDescendant_walks_toward_root()
    {
        using Tree tree = Tree("[1, 2]");
        Node root = tree.RootNode;
        Node array = root.NamedChild(0);
        Node number = array.NamedChild(0);
        Node direct = root.ChildWithDescendant(number);
        Assert.Equal("array", direct.Kind);
    }

    [Fact]
    public void Children_and_named_children_enumerables()
    {
        using Tree tree = Tree("[1, 2, 3]");
        Node array = tree.RootNode.NamedChild(0);
        List<Node> all = array.Children.ToList();
        List<Node> named = array.NamedChildren.ToList();
        Assert.Equal((int)array.ChildCount, all.Count);
        Assert.Equal((int)array.NamedChildCount, named.Count);
        Assert.True(all.Count > named.Count); // brackets + commas are anonymous
    }

    [Fact]
    public void Field_name_for_child_and_field_lookups()
    {
        using Tree tree = Tree("{\"key\": 1}");
        Node obj = tree.RootNode.NamedChild(0);
        Node pair = obj.NamedChild(0);
        Assert.Equal("pair", pair.Kind);

        Node key = pair.ChildByFieldName("key");
        Assert.Equal("string", key.Kind);
        Node value = pair.ChildByFieldName("value");
        Assert.Equal("number", value.Kind);

        // ChildByFieldId via the language's field id.
        ushort keyId = Grammars.Json.FieldIdForName("key");
        Assert.True(keyId > 0);
        Node keyById = pair.ChildByFieldId(keyId);
        Assert.True(keyById.Equals(key));

        // Field name for child / named child.
        uint keyIndex = 0;
        for (uint i = 0; i < pair.ChildCount; i++)
            if (pair.FieldNameForChild(i) == "key") keyIndex = i;
        Assert.Equal("key", pair.FieldNameForChild(keyIndex));

        // A child with no field returns null name (e.g. the ':' token has no field).
        bool sawNullField = false;
        for (uint i = 0; i < pair.ChildCount; i++)
            if (pair.FieldNameForChild(i) is null) sawNullField = true;
        Assert.True(sawNullField);

        // Named-child field names.
        Assert.Equal("key", pair.FieldNameForNamedChild(0));
    }

    [Fact]
    public void ChildByFieldName_null_throws()
    {
        using Tree tree = Tree("{\"k\": 1}");
        Node pair = tree.RootNode.NamedChild(0).NamedChild(0);
        Assert.Throws<ArgumentNullException>(() => pair.ChildByFieldName(null!));
    }

    [Fact]
    public void ChildByFieldName_long_name_uses_heap_buffer()
    {
        using Tree tree = Tree("{\"k\": 1}");
        Node pair = tree.RootNode.NamedChild(0).NamedChild(0);
        // > 256 bytes forces the heap buffer branch; the field doesn't exist -> null node.
        string longName = new string('x', 300);
        Assert.True(pair.ChildByFieldName(longName).IsNull);
    }

    [Fact]
    public void Siblings()
    {
        using Tree tree = Tree("[1, 2, 3]");
        Node array = tree.RootNode.NamedChild(0);
        Node first = array.NamedChild(0);
        Node second = first.NextNamedSibling;
        Assert.Equal("number", second.Kind);
        Assert.True(second.PrevNamedSibling.Equals(first));

        Node openBracket = array.Child(0);
        Node afterBracket = openBracket.NextSibling;
        Assert.False(afterBracket.IsNull);
        Assert.True(afterBracket.PrevSibling.Equals(openBracket));
    }

    [Fact]
    public void FirstChildForByte_variants()
    {
        using Tree tree = Tree("[1, 2, 3]");
        Node array = tree.RootNode.NamedChild(0);
        Node child = array.FirstChildForByte(array.NamedChild(1).StartByte);
        Assert.False(child.IsNull);
        Node namedChild = array.FirstNamedChildForByte(array.NamedChild(1).StartByte);
        Assert.False(namedChild.IsNull);
    }

    [Fact]
    public void Descendant_for_byte_and_point_ranges()
    {
        using Tree tree = Tree("[1, 2, 3]");
        Node root = tree.RootNode;
        Node numberByByte = root.DescendantForByteRange(1, 2);
        Assert.Equal("number", numberByByte.Kind);

        Node numberByPoint = root.DescendantForPointRange(new Point(0, 1), new Point(0, 2));
        Assert.Equal("number", numberByPoint.Kind);

        Node namedByByte = root.NamedDescendantForByteRange(1, 2);
        Assert.Equal("number", namedByByte.Kind);
        Node namedByPoint = root.NamedDescendantForPointRange(new Point(0, 1), new Point(0, 2));
        Assert.Equal("number", namedByPoint.Kind);
    }

    [Fact]
    public void Edit_returns_new_node_with_shifted_position()
    {
        using Tree tree = Tree("[1]");
        Node root = tree.RootNode;
        Node edited = root.Edit(new InputEdit(0, 0, 3, new Point(0, 0), new Point(0, 0), new Point(0, 3)));
        Assert.True(edited.StartByte >= root.StartByte);
    }

    [Fact]
    public void Edit_on_null_node_returns_self()
    {
        Node nul = default;
        Node edited = nul.Edit(new InputEdit(0, 0, 1, Point.Zero, Point.Zero, new Point(0, 1)));
        Assert.True(edited.IsNull);
    }

    [Fact]
    public void Language_property()
    {
        using Tree tree = Tree("[1]");
        Language? lang = tree.RootNode.Language;
        Assert.NotNull(lang);
        Assert.Same(Grammars.Json, lang);
    }

    [Fact]
    public void Text_and_text_span()
    {
        using Tree tree = Tree("[123, 456]");
        Node array = tree.RootNode.NamedChild(0);
        Node first = array.NamedChild(0);
        Assert.Equal("123", first.Text);
        Assert.Equal("123"u8.ToArray(), first.TextSpan.ToArray());
    }

    [Fact]
    public void ToSExpression_and_to_string()
    {
        using Tree tree = Tree("[1]");
        string sexp = tree.RootNode.ToSExpression();
        Assert.Contains("document", sexp);
        Assert.Contains("array", sexp);
        Assert.Equal(sexp, tree.RootNode.ToString());
    }

    [Fact]
    public void Walk_returns_cursor_on_node()
    {
        using Tree tree = Tree("[1]");
        using TreeCursor cursor = tree.RootNode.Walk();
        Assert.Equal("document", cursor.Current.Kind);
    }

    [Fact]
    public void Equality_hashcode_and_operators()
    {
        using Tree tree = Tree("[1, 2]");
        Node a = tree.RootNode;
        Node b = tree.RootNode;
        Assert.True(a.Equals(b));
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a.Equals((object)b));
        Assert.False(a.Equals("not a node"));

        Node child = a.NamedChild(0);
        Assert.False(a == child);
        Assert.True(a != child);
    }

    [Fact]
    public void Null_node_behavior()
    {
        Node def = default;
        Assert.True(def.IsNull);
        Assert.Equal(string.Empty, def.Kind);
        Assert.Null(def.GrammarKind);
        Assert.Equal("(null)", def.ToString());
        Assert.Equal(string.Empty, def.Text);
        Assert.True(def.TextSpan.IsEmpty);
        Assert.Null(def.Language);
        Assert.Equal(string.Empty, def.ToSExpression());

        Node other = default;
        Assert.True(def.Equals(other));   // two null nodes are equal
        Assert.True(def == other);
    }

    [Fact]
    public void Null_node_not_equal_to_real_node()
    {
        using Tree tree = Tree("[1]");
        Node real = tree.RootNode;
        Node nul = default;
        Assert.False(real.Equals(nul));
        Assert.False(nul.Equals(real));
    }

    [Fact]
    public void Parent_of_root_is_null()
    {
        using Tree tree = Tree("[1]");
        Assert.True(tree.RootNode.Parent.IsNull);
    }

    [Fact]
    public void Tree_attached_null_node_equals_and_hashes_like_default()
    {
        using Tree tree = Tree("[1]");

        // A tree-ATTACHED null node: the parent of the root carries the owning tree
        // reference but has a zero underlying node id, so it is IsNull yet not default.
        Node attachedNull = tree.RootNode.Parent;
        Assert.True(attachedNull.IsNull);

        // Another flavour of tree-attached null: an absent field child.
        Node absentField = tree.RootNode.ChildByFieldName("no_such_field");
        Assert.True(absentField.IsNull);

        Node def = default;

        // Equals already treats all null nodes as equal; GetHashCode must agree (contract).
        Assert.True(attachedNull.Equals(def));
        Assert.True(def.Equals(attachedNull));
        Assert.Equal(def.GetHashCode(), attachedNull.GetHashCode());
        Assert.Equal(def.GetHashCode(), absentField.GetHashCode());
        Assert.Equal(0, def.GetHashCode());

        // ...so they collapse to a single entry when used as HashSet<Node> keys.
        var set = new HashSet<Node> { def, attachedNull, absentField };
        Assert.Single(set);
        Assert.Contains(def, set);
        Assert.Contains(attachedNull, set);
    }

    [Fact]
    public void TextSpan_empty_when_range_out_of_bounds()
    {
        // RootNodeWithOffset pushes byte offsets beyond the retained source, so
        // TextSpan must clamp/return empty rather than throw.
        using Tree tree = Tree("[1]");
        Node shifted = tree.RootNodeWithOffset(1000, Point.Zero);
        Assert.True(shifted.TextSpan.IsEmpty);
    }

    [Fact]
    public void TextSpan_clamps_when_end_exceeds_source()
    {
        // A small offset puts StartByte inside the source but EndByte past its end,
        // exercising the clamp (end = source.Length) branch in TextSpan.
        using Tree tree = Tree("[1]"); // 3 bytes
        Node shifted = tree.RootNodeWithOffset(2, Point.Zero);
        Assert.True(shifted.StartByte < 3);
        Assert.True(shifted.EndByte > 3);
        ReadOnlySpan<byte> span = shifted.TextSpan;
        // Clamped to the available bytes [2, 3).
        Assert.Equal(1, span.Length);
    }
}
