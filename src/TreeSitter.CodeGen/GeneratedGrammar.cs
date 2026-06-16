namespace TreeSitter.CodeGen;

/// <summary>A single generated C# source file: its file name and contents.</summary>
/// <param name="FileName">The suggested file name (e.g. <c>Json.Nodes.g.cs</c>).</param>
/// <param name="Source">The complete C# source text.</param>
public readonly record struct GeneratedFile(string FileName, string Source);

/// <summary>
/// The result of generating typed bindings for one grammar: the emitted source
/// file(s) plus summary counts useful for reporting/tests.
/// </summary>
public sealed class GeneratedGrammar
{
    /// <summary>Creates a new <see cref="GeneratedGrammar"/>.</summary>
    /// <param name="files">The generated source files.</param>
    /// <param name="concreteNodeCount">Number of concrete named node structs emitted.</param>
    /// <param name="supertypeCount">Number of supertype structs emitted.</param>
    /// <param name="unnamedNodeCount">Number of unnamed-token structs emitted.</param>
    /// <param name="anonUnionCount">Number of distinct anonymous-union structs emitted.</param>
    public GeneratedGrammar(
        IReadOnlyList<GeneratedFile> files,
        int concreteNodeCount,
        int supertypeCount,
        int unnamedNodeCount,
        int anonUnionCount)
    {
        Files = files;
        ConcreteNodeCount = concreteNodeCount;
        SupertypeCount = supertypeCount;
        UnnamedNodeCount = unnamedNodeCount;
        AnonUnionCount = anonUnionCount;
    }

    /// <summary>The generated source files.</summary>
    public IReadOnlyList<GeneratedFile> Files { get; }

    /// <summary>Number of concrete named node structs emitted.</summary>
    public int ConcreteNodeCount { get; }

    /// <summary>Number of supertype structs emitted.</summary>
    public int SupertypeCount { get; }

    /// <summary>Number of unnamed-token structs emitted.</summary>
    public int UnnamedNodeCount { get; }

    /// <summary>Number of distinct anonymous-union structs emitted.</summary>
    public int AnonUnionCount { get; }

    /// <summary>Total number of typed structs emitted (all categories, incl. unions).</summary>
    public int TotalTypeCount => ConcreteNodeCount + SupertypeCount + UnnamedNodeCount + AnonUnionCount;
}
