namespace TreeSitter;

/// <summary>
/// A zero-based position in a source document expressed as a (row, column) pair.
/// Columns are measured in <b>bytes</b> from the start of the row (UTF-8 is the
/// canonical encoding for this binding).
/// </summary>
/// <param name="Row">The zero-based line number.</param>
/// <param name="Column">The zero-based byte offset within the line.</param>
public readonly record struct Point(uint Row, uint Column)
{
    /// <summary>The origin position (row 0, column 0).</summary>
    public static Point Zero => new(0, 0);

    internal Point(Native.TSPoint p) : this(p.Row, p.Column) { }

    internal Native.TSPoint ToNative() => new(Row, Column);

    /// <inheritdoc/>
    public override string ToString() => $"({Row}, {Column})";
}

/// <summary>
/// A contiguous span of a source document, described both by byte offsets and by
/// (row, column) positions.
/// </summary>
/// <param name="StartPoint">The position of the first byte of the range.</param>
/// <param name="EndPoint">The position one past the last byte of the range.</param>
/// <param name="StartByte">The byte offset of the start of the range.</param>
/// <param name="EndByte">The byte offset one past the end of the range.</param>
public readonly record struct Range(Point StartPoint, Point EndPoint, uint StartByte, uint EndByte)
{
    internal Range(Native.TSRange r)
        : this(new Point(r.StartPoint), new Point(r.EndPoint), r.StartByte, r.EndByte) { }

    internal Native.TSRange ToNative() => new()
    {
        StartPoint = StartPoint.ToNative(),
        EndPoint = EndPoint.ToNative(),
        StartByte = StartByte,
        EndByte = EndByte,
    };

    /// <summary>The number of bytes spanned by this range.</summary>
    public uint ByteLength => EndByte - StartByte;
}

/// <summary>
/// A description of a single edit to a source document, used to keep a
/// <see cref="Tree"/> in sync with edited source before re-parsing.
/// </summary>
/// <remarks>
/// The invariants <c>StartByte &lt;= OldEndByte</c> and <c>StartPoint &lt;= OldEndPoint</c>
/// must hold; violating them produces undefined results from tree-sitter.
/// </remarks>
/// <param name="StartByte">Byte offset where the edited region begins.</param>
/// <param name="OldEndByte">Byte offset where the edited region ended before the edit.</param>
/// <param name="NewEndByte">Byte offset where the edited region ends after the edit.</param>
/// <param name="StartPoint">Position where the edited region begins.</param>
/// <param name="OldEndPoint">Position where the edited region ended before the edit.</param>
/// <param name="NewEndPoint">Position where the edited region ends after the edit.</param>
public readonly record struct InputEdit(
    uint StartByte,
    uint OldEndByte,
    uint NewEndByte,
    Point StartPoint,
    Point OldEndPoint,
    Point NewEndPoint)
{
    internal Native.TSInputEdit ToNative() => new()
    {
        StartByte = StartByte,
        OldEndByte = OldEndByte,
        NewEndByte = NewEndByte,
        StartPoint = StartPoint.ToNative(),
        OldEndPoint = OldEndPoint.ToNative(),
        NewEndPoint = NewEndPoint.ToNative(),
    };
}
