using TreeSitter.CodeGen.Naming;

namespace TreeSitter.Tests;

public class IdentifiersTests
{
    [Theory]
    [InlineData("identifier", "Identifier")]
    [InlineData("binary_expression", "BinaryExpression")]
    [InlineData("snake_case_name", "SnakeCaseName")]
    public void ToTypeName_snake_to_pascal(string input, string expected)
    {
        Assert.Equal(expected, Identifiers.ToTypeName(input));
    }

    [Fact]
    public void ToTypeName_strips_single_leading_underscore()
    {
        // The supertype/hidden convention: a single leading underscore is stripped.
        Assert.Equal("Value", Identifiers.ToTypeName("_value"));
        Assert.Equal("Expression", Identifiers.ToTypeName("_expression"));
    }

    [Theory]
    [InlineData("+", "Plus")]
    [InlineData("-", "Minus")]
    [InlineData("*", "Star")]
    [InlineData("/", "Slash")]
    [InlineData("<", "Lt")]
    [InlineData(">", "Gt")]
    [InlineData("==", "EqEq")]
    [InlineData("<<", "LtLt")]
    [InlineData("&&", "AmpAmp")]
    [InlineData("!", "Bang")]
    [InlineData("?", "Question")]
    [InlineData(".", "Dot")]
    [InlineData("(", "LParen")]
    [InlineData("{", "LBrace")]
    public void ToTypeName_punctuation_table(string input, string expected)
    {
        Assert.Equal(expected, Identifiers.ToTypeName(input));
    }

    [Fact]
    public void ToTypeName_empty_becomes_Empty()
    {
        Assert.Equal("Empty", Identifiers.ToTypeName(""));
    }

    [Fact]
    public void ToTypeName_leading_digit_gets_underscore()
    {
        // "3d" -> Pascal "3d" starts with a digit -> "_3d".
        Assert.StartsWith("_", Identifiers.ToTypeName("3d"));
    }

    [Fact]
    public void ToTypeName_pascalcases_keyword_like_words()
    {
        // Type names are PascalCased, which lifts them out of the (lowercase) keyword
        // set, so 'int' -> 'Int' (no escaping needed for a type name).
        Assert.Equal("Int", Identifiers.ToTypeName("int"));
        Assert.Equal("Void", Identifiers.ToTypeName("void"));
    }

    [Fact]
    public void ToTypeName_exotic_chars_become_U_hex()
    {
        // A non-mapped, non-alnum char (e.g. the section sign) becomes U{hex}.
        string result = Identifiers.ToTypeName("§"); // § = U+00A7
        Assert.Contains("UA7", result);
    }

    [Fact]
    public void ToTypeName_control_char_becomes_U_hex()
    {
        string result = Identifiers.ToTypeName("ab");
        Assert.Contains("U1", result);
    }

    [Fact]
    public void ToMemberName_pascalcases()
    {
        Assert.Equal("Condition", Identifiers.ToMemberName("condition"));
        Assert.Equal("Operator", Identifiers.ToMemberName("operator"));
        // Member names are PascalCased too, lifting them out of the keyword set.
        Assert.Equal("Static", Identifiers.ToMemberName("static"));
        Assert.Equal("Return", Identifiers.ToMemberName("return"));
    }

    [Fact]
    public void ToMemberName_empty_becomes_Member()
    {
        Assert.Equal("Member", Identifiers.ToMemberName(""));
    }

    [Fact]
    public void ToMemberName_leading_digit_gets_underscore()
    {
        Assert.StartsWith("_", Identifiers.ToMemberName("2nd_operand"));
    }

    [Fact]
    public void IsKeyword()
    {
        Assert.True(Identifiers.IsKeyword("class"));
        Assert.True(Identifiers.IsKeyword("int"));
        Assert.False(Identifiers.IsKeyword("Class"));
        Assert.False(Identifiers.IsKeyword("identifier"));
    }

    [Fact]
    public void ContainsPunctuation()
    {
        Assert.True(Identifiers.ContainsPunctuation("+"));
        Assert.True(Identifiers.ContainsPunctuation("<<"));
        Assert.True(Identifiers.ContainsPunctuation("a b")); // space counts
        Assert.False(Identifiers.ContainsPunctuation("await"));
        Assert.False(Identifiers.ContainsPunctuation("snake_case")); // underscore is ok
        Assert.False(Identifiers.ContainsPunctuation("abc123"));
    }

    [Fact]
    public void ToTypeName_camel_hump_splitting()
    {
        // A lower/digit followed by an upper starts a new word.
        Assert.Equal("XmlHttpRequest", Identifiers.ToTypeName("xmlHttpRequest"));
    }
}

public class NameAllocatorTests
{
    [Fact]
    public void Allocate_is_unique_and_deterministic()
    {
        var alloc = new NameAllocator();
        Assert.Equal("Foo", alloc.Allocate("Foo"));
        Assert.Equal("Foo_", alloc.Allocate("Foo"));
        Assert.Equal("Foo__", alloc.Allocate("Foo"));
        Assert.True(alloc.Contains("Foo"));
        Assert.True(alloc.Contains("Foo_"));
        Assert.False(alloc.Contains("Bar"));
    }

    [Fact]
    public void Reserved_names_are_taken_from_the_start()
    {
        var alloc = new NameAllocator(["Node", "Kind"]);
        Assert.True(alloc.Contains("Node"));
        Assert.Equal("Node_", alloc.Allocate("Node"));
        Assert.Equal("Kind_", alloc.Allocate("Kind"));
        Assert.Equal("Other", alloc.Allocate("Other"));
    }

    [Fact]
    public void Determinism_for_fixed_insertion_order()
    {
        static List<string> Run()
        {
            var a = new NameAllocator();
            return new List<string> { a.Allocate("X"), a.Allocate("X"), a.Allocate("Y"), a.Allocate("X") };
        }
        Assert.Equal(Run(), Run());
        Assert.Equal(["X", "X_", "Y", "X__"], Run());
    }
}
