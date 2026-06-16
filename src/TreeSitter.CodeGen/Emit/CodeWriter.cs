using System.Text;

namespace TreeSitter.CodeGen.Emit;

/// <summary>A tiny indentation-aware source-text builder used by the emitters.</summary>
public sealed class CodeWriter
{
    private readonly StringBuilder _sb = new();
    private int _indent;

    /// <summary>Increases the indentation level by one.</summary>
    public CodeWriter Indent() { _indent++; return this; }

    /// <summary>Decreases the indentation level by one (never below zero).</summary>
    public CodeWriter Outdent() { if (_indent > 0) _indent--; return this; }

    /// <summary>Appends a blank line.</summary>
    public CodeWriter Blank() { _sb.Append('\n'); return this; }

    /// <summary>Appends a single indented line.</summary>
    /// <param name="text">The line text (no trailing newline needed).</param>
    public CodeWriter Line(string text)
    {
        if (text.Length == 0)
        {
            _sb.Append('\n');
            return this;
        }
        for (int i = 0; i < _indent; i++)
            _sb.Append("    ");
        _sb.Append(text).Append('\n');
        return this;
    }

    /// <summary>Appends a raw, possibly multi-line block, indenting each non-empty line.</summary>
    /// <param name="block">The text block.</param>
    public CodeWriter Block(string block)
    {
        foreach (string line in block.Split('\n'))
            Line(line);
        return this;
    }

    /// <summary>Opens a brace-delimited scope: writes <paramref name="header"/> then <c>{</c> and indents.</summary>
    /// <param name="header">The line preceding the opening brace.</param>
    public CodeWriter OpenBrace(string header)
    {
        Line(header);
        Line("{");
        Indent();
        return this;
    }

    /// <summary>Closes a brace-delimited scope: outdents and writes <c>}</c>.</summary>
    public CodeWriter CloseBrace()
    {
        Outdent();
        Line("}");
        return this;
    }

    /// <inheritdoc/>
    public override string ToString() => _sb.ToString();
}
