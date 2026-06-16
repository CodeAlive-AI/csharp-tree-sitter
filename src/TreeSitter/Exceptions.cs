namespace TreeSitter;

/// <summary>Base type for all exceptions thrown by the TreeSitter binding.</summary>
public class TreeSitterException : Exception
{
    /// <summary>Creates a new <see cref="TreeSitterException"/>.</summary>
    public TreeSitterException() { }

    /// <summary>Creates a new <see cref="TreeSitterException"/> with a message.</summary>
    public TreeSitterException(string message) : base(message) { }

    /// <summary>Creates a new <see cref="TreeSitterException"/> with a message and inner exception.</summary>
    public TreeSitterException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when a query fails to compile. Carries the byte
/// <see cref="Offset"/> within the query source where the problem was detected
/// and the <see cref="Error"/> classification.
/// </summary>
public sealed class QueryException : TreeSitterException
{
    /// <summary>The byte offset within the query source where the error was detected.</summary>
    public uint Offset { get; }

    /// <summary>The classification of the query error.</summary>
    public QueryError Error { get; }

    /// <summary>Creates a new <see cref="QueryException"/>.</summary>
    /// <param name="offset">The byte offset within the query source of the error.</param>
    /// <param name="error">The classification of the error.</param>
    public QueryException(uint offset, QueryError error)
        : base($"Failed to compile query: {error} at byte offset {offset}.")
    {
        Offset = offset;
        Error = error;
    }
}

/// <summary>
/// Thrown when assigning a <see cref="Language"/> to a <see cref="Parser"/> fails
/// because the language's ABI version is incompatible with this build of the
/// tree-sitter runtime.
/// </summary>
public sealed class LanguageVersionException : TreeSitterException
{
    /// <summary>The ABI version reported by the offending language, if known.</summary>
    public uint? AbiVersion { get; }

    /// <summary>Creates a new <see cref="LanguageVersionException"/>.</summary>
    public LanguageVersionException()
        : base("The language's ABI version is incompatible with this tree-sitter runtime.") { }

    /// <summary>Creates a new <see cref="LanguageVersionException"/> with the offending ABI version.</summary>
    /// <param name="abiVersion">The ABI version reported by the language.</param>
    public LanguageVersionException(uint abiVersion)
        : base($"The language's ABI version ({abiVersion}) is incompatible with this tree-sitter runtime " +
               $"(supports {TreeSitterConstants.MinCompatibleAbiVersion}..{TreeSitterConstants.AbiVersion}).")
    {
        AbiVersion = abiVersion;
    }
}

/// <summary>ABI / version constants for the bundled tree-sitter runtime.</summary>
public static class TreeSitterConstants
{
    /// <summary>The latest language ABI version supported by this runtime (15).</summary>
    public const uint AbiVersion = 15;

    /// <summary>The earliest language ABI version supported by this runtime (13).</summary>
    public const uint MinCompatibleAbiVersion = 13;
}
