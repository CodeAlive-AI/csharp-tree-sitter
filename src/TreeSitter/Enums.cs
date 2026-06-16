namespace TreeSitter;

/// <summary>How the bytes of a source document are encoded. Mirrors <c>TSInputEncoding</c>.</summary>
public enum InputEncoding
{
    /// <summary>UTF-8 (the canonical encoding for this binding).</summary>
    Utf8 = 0,
    /// <summary>UTF-16, little-endian.</summary>
    Utf16LittleEndian = 1,
    /// <summary>UTF-16, big-endian.</summary>
    Utf16BigEndian = 2,
    /// <summary>A custom encoding decoded via a user-supplied decode function.</summary>
    Custom = 3,
}

/// <summary>The category of a grammar symbol. Mirrors <c>TSSymbolType</c>.</summary>
public enum SymbolType
{
    /// <summary>A named rule.</summary>
    Regular = 0,
    /// <summary>A string literal / punctuation (anonymous) node.</summary>
    Anonymous = 1,
    /// <summary>An abstract supertype rule.</summary>
    Supertype = 2,
    /// <summary>An internal / hidden node that is never returned from the API.</summary>
    Auxiliary = 3,
}

/// <summary>The kind of message emitted by a parser logger. Mirrors <c>TSLogType</c>.</summary>
public enum LogType
{
    /// <summary>A message about the parsing (LR) process.</summary>
    Parse = 0,
    /// <summary>A message about the lexing (tokenization) process.</summary>
    Lex = 1,
}

/// <summary>
/// The cardinality of a query capture, describing how many nodes a capture may
/// match. Mirrors <c>TSQuantifier</c>.
/// </summary>
public enum Quantifier
{
    /// <summary>The capture matches zero nodes (array initialization sentinel).</summary>
    Zero = 0,
    /// <summary>The capture matches zero or one node (<c>?</c>).</summary>
    ZeroOrOne = 1,
    /// <summary>The capture matches zero or more nodes (<c>*</c>).</summary>
    ZeroOrMore = 2,
    /// <summary>The capture matches exactly one node.</summary>
    One = 3,
    /// <summary>The capture matches one or more nodes (<c>+</c>).</summary>
    OneOrMore = 4,
}

/// <summary>The kind of a query predicate step. Mirrors <c>TSQueryPredicateStepType</c>.</summary>
public enum QueryPredicateStepType
{
    /// <summary>A sentinel marking the end of a single predicate.</summary>
    Done = 0,
    /// <summary>A reference to a named capture (the value id is a capture id).</summary>
    Capture = 1,
    /// <summary>A reference to a literal string (the value id is a string id).</summary>
    String = 2,
}

/// <summary>The kind of error reported when compiling a query. Mirrors <c>TSQueryError</c>.</summary>
public enum QueryError
{
    /// <summary>No error.</summary>
    None = 0,
    /// <summary>The query source has a syntax error.</summary>
    Syntax = 1,
    /// <summary>The query references an unknown node type.</summary>
    NodeType = 2,
    /// <summary>The query references an unknown field.</summary>
    Field = 3,
    /// <summary>The query references an unknown capture.</summary>
    Capture = 4,
    /// <summary>The query has a structural error (e.g. an invalid predicate).</summary>
    Structure = 5,
    /// <summary>The query is incompatible with the language.</summary>
    Language = 6,
}
