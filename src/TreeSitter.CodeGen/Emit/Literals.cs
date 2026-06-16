using System.Globalization;
using System.Text;

namespace TreeSitter.CodeGen.Emit;

/// <summary>Renders C# literals for generated source.</summary>
internal static class Literals
{
    /// <summary>
    /// Renders <paramref name="value"/> as a verbatim-safe C# double-quoted string
    /// literal, escaping quotes, backslashes, and control characters so the output is
    /// always valid regardless of the node type's punctuation (e.g. <c>"</c>,
    /// <c>\</c>, newline tokens).
    /// </summary>
    /// <param name="value">The string to render.</param>
    public static string String(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\0': sb.Append("\\0"); break;
                case '\a': sb.Append("\\a"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\v': sb.Append("\\v"); break;
                default:
                    if (char.IsControl(c))
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
