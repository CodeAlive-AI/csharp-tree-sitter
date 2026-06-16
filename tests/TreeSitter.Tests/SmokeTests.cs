using TreeSitter;

namespace TreeSitter.Tests;

public class SmokeTests
{
    [Fact]
    public void Json_grammar_loads_and_parses()
    {
        using var parser = new Parser(Grammars.Json);
        using Tree? tree = parser.Parse("{\"a\": 1}");
        Assert.NotNull(tree);
        Assert.Equal("document", tree!.RootNode.Kind);
        Assert.False(tree.RootNode.HasError);
    }

    [Fact]
    public void Python_grammar_loads()
    {
        using var parser = new Parser(Grammars.Python);
        using Tree? tree = parser.Parse("x = 1\n");
        Assert.NotNull(tree);
        Assert.Equal("module", tree!.RootNode.Kind);
    }
}
