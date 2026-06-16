using TreeSitter;
using TreeSitter.Typed;

namespace TreeSitter.Tests;

public class ValueTypesTests
{
    [Fact]
    public void Point_members_equality_tostring()
    {
        var a = new Point(2, 5);
        Assert.Equal(2u, a.Row);
        Assert.Equal(5u, a.Column);
        Assert.Equal(new Point(2, 5), a);
        Assert.NotEqual(new Point(2, 6), a);
        Assert.Equal("(2, 5)", a.ToString());
        Assert.Equal(new Point(0, 0), Point.Zero);
        Assert.Equal(a.GetHashCode(), new Point(2, 5).GetHashCode());
    }

    [Fact]
    public void Range_members_and_byte_length()
    {
        var range = new Range(new Point(0, 0), new Point(0, 10), 3, 13);
        Assert.Equal(new Point(0, 0), range.StartPoint);
        Assert.Equal(new Point(0, 10), range.EndPoint);
        Assert.Equal(3u, range.StartByte);
        Assert.Equal(13u, range.EndByte);
        Assert.Equal(10u, range.ByteLength);
        Assert.Equal(range, new Range(new Point(0, 0), new Point(0, 10), 3, 13));
    }

    [Fact]
    public void InputEdit_members_and_equality()
    {
        var e = new InputEdit(1, 2, 3, new Point(0, 1), new Point(0, 2), new Point(0, 3));
        Assert.Equal(1u, e.StartByte);
        Assert.Equal(2u, e.OldEndByte);
        Assert.Equal(3u, e.NewEndByte);
        Assert.Equal(new Point(0, 1), e.StartPoint);
        Assert.Equal(new Point(0, 2), e.OldEndPoint);
        Assert.Equal(new Point(0, 3), e.NewEndPoint);
        Assert.Equal(e, new InputEdit(1, 2, 3, new Point(0, 1), new Point(0, 2), new Point(0, 3)));
        Assert.NotEqual(e, new InputEdit(9, 2, 3, new Point(0, 1), new Point(0, 2), new Point(0, 3)));
    }

    [Fact]
    public void QueryCapture_record_struct()
    {
        var c = new QueryCapture(default, 7);
        Assert.Equal(7u, c.Index);
        Assert.True(c.Node.IsNull);
        Assert.Equal(new QueryCapture(default, 7), c);
    }

    [Fact]
    public void QueryPredicateStep_record_struct()
    {
        var s = new QueryPredicateStep(QueryPredicateStepType.Capture, 3);
        Assert.Equal(QueryPredicateStepType.Capture, s.Type);
        Assert.Equal(3u, s.ValueId);
    }
}

public class EnumsTests
{
    [Fact]
    public void InputEncoding_values()
    {
        Assert.Equal(0, (int)InputEncoding.Utf8);
        Assert.Equal(1, (int)InputEncoding.Utf16LittleEndian);
        Assert.Equal(2, (int)InputEncoding.Utf16BigEndian);
        Assert.Equal(3, (int)InputEncoding.Custom);
    }

    [Fact]
    public void SymbolType_values()
    {
        Assert.Equal(0, (int)SymbolType.Regular);
        Assert.Equal(1, (int)SymbolType.Anonymous);
        Assert.Equal(2, (int)SymbolType.Supertype);
        Assert.Equal(3, (int)SymbolType.Auxiliary);
    }

    [Fact]
    public void LogType_values()
    {
        Assert.Equal(0, (int)LogType.Parse);
        Assert.Equal(1, (int)LogType.Lex);
    }

    [Fact]
    public void Quantifier_values()
    {
        Assert.Equal(0, (int)Quantifier.Zero);
        Assert.Equal(1, (int)Quantifier.ZeroOrOne);
        Assert.Equal(2, (int)Quantifier.ZeroOrMore);
        Assert.Equal(3, (int)Quantifier.One);
        Assert.Equal(4, (int)Quantifier.OneOrMore);
    }

    [Fact]
    public void QueryPredicateStepType_values()
    {
        Assert.Equal(0, (int)QueryPredicateStepType.Done);
        Assert.Equal(1, (int)QueryPredicateStepType.Capture);
        Assert.Equal(2, (int)QueryPredicateStepType.String);
    }

    [Fact]
    public void QueryError_values()
    {
        Assert.Equal(0, (int)QueryError.None);
        Assert.Equal(1, (int)QueryError.Syntax);
        Assert.Equal(2, (int)QueryError.NodeType);
        Assert.Equal(3, (int)QueryError.Field);
        Assert.Equal(4, (int)QueryError.Capture);
        Assert.Equal(5, (int)QueryError.Structure);
        Assert.Equal(6, (int)QueryError.Language);
    }
}

public class ExceptionsTests
{
    [Fact]
    public void TreeSitterException_ctors()
    {
        Assert.NotNull(new TreeSitterException().Message);
        Assert.Equal("msg", new TreeSitterException("msg").Message);
        var inner = new InvalidOperationException("inner");
        var ex = new TreeSitterException("outer", inner);
        Assert.Equal("outer", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void QueryException_carries_offset_and_error()
    {
        var ex = new QueryException(42, QueryError.NodeType);
        Assert.Equal(42u, ex.Offset);
        Assert.Equal(QueryError.NodeType, ex.Error);
        Assert.Contains("42", ex.Message);
        Assert.Contains("NodeType", ex.Message);
        Assert.IsAssignableFrom<TreeSitterException>(ex);
    }

    [Fact]
    public void LanguageVersionException_default_and_with_version()
    {
        var def = new LanguageVersionException();
        Assert.Null(def.AbiVersion);
        Assert.NotNull(def.Message);

        var withVer = new LanguageVersionException(99);
        Assert.Equal(99u, withVer.AbiVersion);
        Assert.Contains("99", withVer.Message);
        Assert.Contains(TreeSitterConstants.AbiVersion.ToString(), withVer.Message);
    }

    [Fact]
    public void IncorrectNodeKindException_with_and_without_accepted_kinds()
    {
        using Tree tree = TestData.ParseJson("[1]");
        Node node = tree.RootNode;

        var ex = new IncorrectNodeKindException(node, "Foo");
        Assert.Equal("Foo", ex.ExpectedType);
        Assert.Null(ex.AcceptedKinds);
        Assert.True(ex.Node.Equals(node));
        Assert.Contains("Foo", ex.Message);
        Assert.Contains("document", ex.Message);

        var ex2 = new IncorrectNodeKindException(node, "Bar", "a, b, c");
        Assert.Equal("a, b, c", ex2.AcceptedKinds);
        Assert.Contains("accepts a, b, c", ex2.Message);

        var exNull = new IncorrectNodeKindException(default, "Baz");
        Assert.Contains("(null node)", exNull.Message);
    }

    [Fact]
    public void TreeSitterConstants_values()
    {
        Assert.Equal(15u, TreeSitterConstants.AbiVersion);
        Assert.Equal(13u, TreeSitterConstants.MinCompatibleAbiVersion);
    }
}
