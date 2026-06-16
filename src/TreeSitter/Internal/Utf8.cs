using System.Runtime.InteropServices;
using System.Text;

namespace TreeSitter.Internal;

/// <summary>
/// Internal helpers for converting between managed strings and the UTF-8 byte
/// pointers used throughout the tree-sitter C API. UTF-8 is canonical for this
/// binding, so all string interop goes through here.
/// </summary>
internal static class Utf8
{
    /// <summary>
    /// Marshals a null-terminated UTF-8 <c>const char*</c> owned by the native
    /// library (static lifetime; must NOT be freed) into a managed string.
    /// Returns <see langword="null"/> for a null pointer.
    /// </summary>
    internal static string? PtrToString(IntPtr ptr) =>
        ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);

    /// <summary>
    /// Marshals a UTF-8 string of an explicit byte length (not necessarily
    /// null-terminated) owned by the native library into a managed string.
    /// </summary>
    internal static unsafe string? PtrToString(IntPtr ptr, uint length) =>
        ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr, checked((int)length));

    /// <summary>The number of bytes required to encode <paramref name="value"/> as UTF-8.</summary>
    internal static int ByteCount(string value) => Encoding.UTF8.GetByteCount(value);
}
