namespace TreeSitter.CodeGen;

/// <summary>
/// Options controlling typed-node generation. Immutable; pass to
/// <see cref="NodeTypesGenerator.Generate(string, GeneratorOptions)"/>.
/// </summary>
public sealed class GeneratorOptions
{
    /// <summary>
    /// The root namespace for all generated types (e.g.
    /// <c>TreeSitter.Grammars.Json</c>). Required.
    /// </summary>
    public required string RootNamespace { get; init; }

    /// <summary>
    /// The grammar's human name (e.g. <c>json</c>, <c>python</c>). Used in file
    /// names and the auto-generated header. Required.
    /// </summary>
    public required string LanguageName { get; init; }
}
