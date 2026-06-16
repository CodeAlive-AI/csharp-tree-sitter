namespace TreeSitter;

/// <summary>
/// A single capture produced by a query: a <see cref="Node"/> together with the
/// numeric id of the capture that matched it.
/// </summary>
/// <param name="Node">The captured node.</param>
/// <param name="Index">The capture id (resolvable via <see cref="Query.CaptureNameForId"/>).</param>
public readonly record struct QueryCapture(Node Node, uint Index);

/// <summary>
/// A single match of a query pattern: the pattern index and the set of captures it
/// produced. The captures are copied out of native memory and remain valid after
/// the cursor advances.
/// </summary>
public readonly struct QueryMatch
{
    private readonly QueryCapture[]? _captures;

    /// <summary>The match's id, unique within a single query execution.</summary>
    public uint Id { get; }

    /// <summary>The index of the pattern that matched.</summary>
    public ushort PatternIndex { get; }

    /// <summary>
    /// The captures produced by this match. Always non-null: a <c>default</c> or failed
    /// match yields an empty array.
    /// </summary>
    public QueryCapture[] Captures => _captures ?? [];

    internal QueryMatch(uint id, ushort patternIndex, QueryCapture[] captures)
    {
        Id = id;
        PatternIndex = patternIndex;
        _captures = captures;
    }
}

/// <summary>
/// A single step of a query predicate. A predicate is a sequence of steps
/// terminated by a <see cref="QueryPredicateStepType.Done"/> step; capture and
/// string steps carry a <see cref="ValueId"/> resolvable via the owning
/// <see cref="Query"/>.
/// </summary>
/// <param name="Type">The kind of step.</param>
/// <param name="ValueId">A capture id, string id, or <c>0</c> for a done step.</param>
public readonly record struct QueryPredicateStep(QueryPredicateStepType Type, uint ValueId);
