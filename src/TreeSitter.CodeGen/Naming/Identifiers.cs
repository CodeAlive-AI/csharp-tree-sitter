using System.Globalization;
using System.Text;

namespace TreeSitter.CodeGen.Naming;

/// <summary>
/// Sanitizes tree-sitter node-type strings and field names into valid, idiomatic
/// C# identifiers. Pure and deterministic so the generator output is stable across
/// runs and machines.
/// </summary>
public static class Identifiers
{
    /// <summary>The set of C# reserved keywords (contextual keywords are not included).</summary>
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
        "checked", "class", "const", "continue", "decimal", "default", "delegate",
        "do", "double", "else", "enum", "event", "explicit", "extern", "false",
        "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit",
        "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
        "new", "null", "object", "operator", "out", "override", "params", "private",
        "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
        "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    };

    /// <summary>
    /// Maps punctuation characters to readable PascalCase fragments. Mirrors the
    /// table specified for the binding so emitted names are predictable.
    /// </summary>
    private static readonly IReadOnlyDictionary<char, string> Punctuation = new Dictionary<char, string>
    {
        ['+'] = "Plus", ['-'] = "Minus", ['*'] = "Star", ['/'] = "Slash",
        ['<'] = "Lt", ['>'] = "Gt", ['='] = "Eq", ['!'] = "Bang",
        ['&'] = "Amp", ['|'] = "Pipe", ['%'] = "Percent", ['^'] = "Caret",
        ['~'] = "Tilde", ['?'] = "Question", [':'] = "Colon", ['.'] = "Dot",
        [','] = "Comma", [';'] = "Semicolon", ['('] = "LParen", [')'] = "RParen",
        ['['] = "LBracket", [']'] = "RBracket", ['{'] = "LBrace", ['}'] = "RBrace",
        ['@'] = "At", ['#'] = "Hash", ['$'] = "Dollar", ['\\'] = "Backslash",
        ['"'] = "Quote", ['\''] = "Apostrophe", ['`'] = "Backtick", [' '] = "Space",
    };

    /// <summary>Whether <paramref name="word"/> is a C# reserved keyword.</summary>
    /// <param name="word">The candidate identifier.</param>
    public static bool IsKeyword(string word) => Keywords.Contains(word);

    /// <summary>
    /// Determines whether the s-expression node type <paramref name="type"/> contains
    /// only punctuation characters (no letters/digits). Such unnamed tokens live in
    /// the <c>Symbols</c> namespace rather than <c>Unnamed</c>.
    /// </summary>
    /// <param name="type">The node type string.</param>
    public static bool IsAllPunctuation(string type)
    {
        if (type.Length == 0)
            return false;
        foreach (char c in type)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                return false;
        }
        return true;
    }

    /// <summary>
    /// Converts a node-type s-expression string to a PascalCase C# type identifier,
    /// applying the punctuation table, snake_case splitting, leading-underscore
    /// stripping, digit-prefix and keyword guards. Does not perform cross-name
    /// deduplication (see <see cref="NameAllocator"/>).
    /// </summary>
    /// <param name="type">The node type string.</param>
    public static string ToTypeName(string type)
    {
        // Strip a single leading underscore (the supertype/"hidden" convention).
        string core = type.Length > 0 && type[0] == '_' ? type[1..] : type;

        string pascal = ToPascal(core);
        if (pascal.Length == 0)
            pascal = "Empty";

        if (char.IsDigit(pascal[0]))
            pascal = "_" + pascal;

        // Type names that collide with a keyword get a trailing underscore (we never
        // use the '@' prefix for generated public types).
        if (Keywords.Contains(pascal))
            pascal += "_";

        return pascal;
    }

    /// <summary>
    /// Converts a field name (always a plain snake_case identifier in practice) to a
    /// PascalCase C# member identifier, guarding keywords with a trailing underscore.
    /// </summary>
    /// <param name="fieldName">The field name.</param>
    public static string ToMemberName(string fieldName)
    {
        string pascal = ToPascal(fieldName);
        if (pascal.Length == 0)
            pascal = "Member";
        if (char.IsDigit(pascal[0]))
            pascal = "_" + pascal;
        if (Keywords.Contains(pascal))
            pascal += "_";
        return pascal;
    }

    /// <summary>
    /// The PascalCase transform shared by type and member naming: maps punctuation,
    /// substitutes <c>U{hex}</c> for any other non-identifier character, splits on
    /// underscores and case boundaries, and title-cases each resulting word.
    /// </summary>
    private static string ToPascal(string input)
    {
        if (input.Length == 0)
            return string.Empty;

        // Phase 1: rewrite each character into an alnum/underscore "token stream",
        // inserting underscores around mapped punctuation so words split cleanly.
        var rewritten = new StringBuilder(input.Length * 2);
        foreach (char c in input)
        {
            if (c == '_' || char.IsLetterOrDigit(c))
            {
                rewritten.Append(c);
            }
            else if (Punctuation.TryGetValue(c, out string? frag))
            {
                rewritten.Append('_').Append(frag).Append('_');
            }
            else
            {
                // Any remaining non-identifier char -> U{hexcodepoint}.
                rewritten.Append('_')
                         .Append('U')
                         .Append(((int)c).ToString("X", CultureInfo.InvariantCulture))
                         .Append('_');
            }
        }

        // Phase 2: split into words on underscores AND lower->upper boundaries, then
        // Title-case each word and concatenate.
        string tokens = rewritten.ToString();
        var result = new StringBuilder(tokens.Length);
        var word = new StringBuilder();

        void Flush()
        {
            if (word.Length == 0)
                return;
            result.Append(char.ToUpperInvariant(word[0]));
            for (int i = 1; i < word.Length; i++)
                result.Append(word[i]);
            word.Clear();
        }

        for (int i = 0; i < tokens.Length; i++)
        {
            char c = tokens[i];
            if (c == '_')
            {
                Flush();
                continue;
            }
            // Split camelCase humps: a lower/digit followed by an upper starts a new word.
            if (word.Length > 0 &&
                char.IsUpper(c) &&
                (char.IsLower(word[^1]) || char.IsDigit(word[^1])))
            {
                Flush();
            }
            word.Append(c);
        }
        Flush();

        return result.ToString();
    }
}
