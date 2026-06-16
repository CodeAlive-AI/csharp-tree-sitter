using System.Runtime.InteropServices;

namespace TreeSitter.Native;

// =============================================================================
// Blittable interop structs mirroring tree-sitter's C ABI (v0.26.9, ABI 15).
//
// Field order and types match lib/include/tree_sitter/api.h EXACTLY. Sizes were
// verified against the native library on linux-x64 (see comments). All structs
// are `internal` -- the safe public API (Layer 1) projects these onto idiomatic
// value types (Point, Range, InputEdit, ...).
//
// Default StructLayout for a struct that contains only primitives is Sequential,
// but we annotate explicitly for clarity and to pin the packing where it matters.
// =============================================================================

/// <summary>A (row, column) position. Mirrors <c>TSPoint</c>. Size = 8 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TSPoint
{
    public uint Row;
    public uint Column;

    public TSPoint(uint row, uint column)
    {
        Row = row;
        Column = column;
    }
}

/// <summary>A contiguous range of source. Mirrors <c>TSRange</c>. Size = 24 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TSRange
{
    public TSPoint StartPoint; // offset 0
    public TSPoint EndPoint;   // offset 8
    public uint StartByte;     // offset 16
    public uint EndByte;       // offset 20
}

/// <summary>
/// A description of an edit. Mirrors <c>TSInputEdit</c>.
/// </summary>
/// <remarks>
/// ALIGNMENT SURPRISE: <c>TSPoint</c>'s members are both <c>uint32_t</c>, so the
/// struct has alignment 4 (not 8). Consequently the three leading <c>uint32_t</c>
/// fields are followed immediately by <c>start_point</c> at offset 12 with NO
/// padding. Total size is <b>36 bytes</b> on 64-bit, not 40. We force
/// <c>Pack = 4</c> so the C# layout matches the C layout byte-for-byte.
/// Verified: start_byte@0, old_end_byte@4, new_end_byte@8, start_point@12,
/// old_end_point@20, new_end_point@28, sizeof=36.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct TSInputEdit
{
    public uint StartByte;       // offset 0
    public uint OldEndByte;      // offset 4
    public uint NewEndByte;      // offset 8
    public TSPoint StartPoint;   // offset 12
    public TSPoint OldEndPoint;  // offset 20
    public TSPoint NewEndPoint;  // offset 28
}

/// <summary>
/// A syntax node handle. Mirrors <c>TSNode</c>. Size = 32 bytes
/// (uint32[4] context + 2 pointers). Passed BY VALUE everywhere in the API.
/// </summary>
/// <remarks>
/// A <c>TSNode</c> carries no ownership and requires no free; it is only valid
/// while its owning <c>TSTree</c> is alive.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct TSNode : IEquatable<TSNode>
{
    public uint Context0; // offset 0
    public uint Context1; // offset 4
    public uint Context2; // offset 8
    public uint Context3; // offset 12
    public IntPtr Id;     // offset 16 -- uniquely identifies the node
    public IntPtr Tree;   // offset 24 -- pointer back to owning tree

    /// <summary>A node whose <c>id</c> is null represents the absence of a node.</summary>
    public readonly bool IsZero => Id == IntPtr.Zero;

    public readonly bool Equals(TSNode other) =>
        Context0 == other.Context0 &&
        Context1 == other.Context1 &&
        Context2 == other.Context2 &&
        Context3 == other.Context3 &&
        Id == other.Id &&
        Tree == other.Tree;

    public override readonly bool Equals(object? obj) => obj is TSNode other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(Id, Tree);
}

/// <summary>
/// A tree cursor's state. Mirrors <c>TSTreeCursor</c>. Size = 32 bytes
/// (2 pointers + uint32[3] + 4 bytes trailing padding). Must be released with
/// <c>ts_tree_cursor_delete</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TSTreeCursor
{
    public IntPtr Tree;     // offset 0
    public IntPtr Id;       // offset 8
    public uint Context0;   // offset 16
    public uint Context1;   // offset 20
    public uint Context2;   // offset 24
    // 4 bytes trailing padding to a pointer boundary -> total 32 bytes.
}

/// <summary>A single capture within a query match. Mirrors <c>TSQueryCapture</c>. Size = 40 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TSQueryCapture
{
    public TSNode Node; // offset 0 (32 bytes)
    public uint Index;  // offset 32 (+4 bytes trailing padding -> 40)
}

/// <summary>
/// A query match. Mirrors <c>TSQueryMatch</c>. Size = 16 bytes. The
/// <see cref="Captures"/> pointer is owned by the cursor and is only valid until
/// the next <c>ts_query_cursor_next_*</c> call.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TSQueryMatch
{
    public uint Id;             // offset 0
    public ushort PatternIndex; // offset 4
    public ushort CaptureCount; // offset 6
    public IntPtr Captures;     // offset 8 -- const TSQueryCapture*
}

/// <summary>A single step of a query predicate. Mirrors <c>TSQueryPredicateStep</c>. Size = 8 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TSQueryPredicateStep
{
    public int Type;      // TSQueryPredicateStepType (int-sized enum), offset 0
    public uint ValueId;  // offset 4
}

/// <summary>Semantic version metadata for a language. Mirrors <c>TSLanguageMetadata</c>. Size = 3 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TSLanguageMetadata
{
    public byte MajorVersion;
    public byte MinorVersion;
    public byte PatchVersion;
}

/// <summary>
/// Progress state passed to a parse progress callback. Mirrors
/// <c>TSParseState</c>. Size = 16 bytes (pointer, uint32, bool + padding).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TSParseState
{
    public IntPtr Payload;            // offset 0
    public uint CurrentByteOffset;    // offset 8
    [MarshalAs(UnmanagedType.U1)]
    public bool HasError;             // offset 12 (+3 padding -> 16)
}

/// <summary>
/// Options for <c>ts_parser_parse_with_options</c>. Mirrors <c>TSParseOptions</c>.
/// Size = 16 bytes. <see cref="ProgressCallback"/> is an unmanaged
/// <c>bool (*)(TSParseState*)</c> function pointer.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct TSParseOptions
{
    public IntPtr Payload;                                           // offset 0
    public delegate* unmanaged[Cdecl]<TSParseState*, byte> ProgressCallback; // offset 8
}

/// <summary>
/// Progress state passed to a query progress callback. Mirrors
/// <c>TSQueryCursorState</c>. Size = 16 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TSQueryCursorState
{
    public IntPtr Payload;         // offset 0
    public uint CurrentByteOffset; // offset 8 (+4 padding -> 16)
}

/// <summary>
/// Options for <c>ts_query_cursor_exec_with_options</c>. Mirrors
/// <c>TSQueryCursorOptions</c>. Size = 16 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct TSQueryCursorOptions
{
    public IntPtr Payload;                                                // offset 0
    public delegate* unmanaged[Cdecl]<TSQueryCursorState*, byte> ProgressCallback; // offset 8
}

/// <summary>
/// A parser logger. Mirrors <c>TSLogger</c>. Size = 16 bytes. Passed and
/// returned by value. The <c>log</c> field is an unmanaged
/// <c>void (*)(void* payload, TSLogType, const char*)</c> function pointer.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct TSLogger
{
    public IntPtr Payload;                                       // offset 0
    public delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void> Log; // offset 8
}

/// <summary>
/// A custom text input. Mirrors <c>TSInput</c>. Size = 32 bytes.
/// </summary>
/// <remarks>
/// Layout: payload@0, read@8, encoding@16 (int, +4 padding), decode@24.
/// <see cref="Read"/> is <c>const char* (*)(void*, uint32_t, TSPoint, uint32_t*)</c>
/// and <see cref="Decode"/> is the ABI-14+ <c>TSDecodeFunction</c>
/// (<c>uint32_t (*)(const uint8_t*, uint32_t, int32_t*)</c>). The library reads
/// UTF-8 chunked input through this struct.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct TSInput
{
    public IntPtr Payload;                                                // offset 0
    public delegate* unmanaged[Cdecl]<IntPtr, uint, TSPoint, uint*, IntPtr> Read; // offset 8
    public int Encoding;                                                  // offset 16 (TSInputEncoding, +4 padding)
    public delegate* unmanaged[Cdecl]<byte*, uint, int*, uint> Decode;    // offset 24
}
