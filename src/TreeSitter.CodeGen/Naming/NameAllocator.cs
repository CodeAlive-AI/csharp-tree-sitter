namespace TreeSitter.CodeGen.Naming;

/// <summary>
/// Allocates unique C# identifiers within a single scope (a namespace, or the
/// member set of one type). When a candidate is already taken it appends <c>_</c>
/// until free, guaranteeing determinism for a fixed insertion order.
/// </summary>
public sealed class NameAllocator
{
    private readonly HashSet<string> _used;

    /// <summary>Creates an allocator, optionally seeded with reserved names.</summary>
    /// <param name="reserved">Names that are considered taken from the start.</param>
    public NameAllocator(IEnumerable<string>? reserved = null)
    {
        _used = reserved is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(reserved, StringComparer.Ordinal);
    }

    /// <summary>
    /// Reserves and returns a unique identifier based on <paramref name="candidate"/>,
    /// appending <c>_</c> as needed to avoid collisions with previously allocated or
    /// reserved names.
    /// </summary>
    /// <param name="candidate">The desired identifier.</param>
    public string Allocate(string candidate)
    {
        string name = candidate;
        while (!_used.Add(name))
            name += "_";
        return name;
    }

    /// <summary>Whether <paramref name="name"/> is already taken.</summary>
    /// <param name="name">The identifier to test.</param>
    public bool Contains(string name) => _used.Contains(name);
}
