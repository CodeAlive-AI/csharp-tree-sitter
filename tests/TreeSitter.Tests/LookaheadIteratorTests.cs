using TreeSitter;

namespace TreeSitter.Tests;

public class LookaheadIteratorTests
{
    /// <summary>Finds a parse state for which a lookahead iterator can be created.</summary>
    private static ushort ValidState()
    {
        Language lang = Grammars.Json;
        using Tree tree = TestData.ParseJson("{\"key\": [1, 2, 3], \"b\": true}");
        // Walk the tree collecting parse states; return the first that yields a valid iterator.
        using TreeCursor cursor = tree.RootNode.Walk();
        var states = new List<ushort>();
        void Visit()
        {
            states.Add(cursor.Current.ParseState);
            states.Add(cursor.Current.NextParseState);
            if (cursor.GotoFirstChild())
            {
                do { Visit(); } while (cursor.GotoNextSibling());
                cursor.GotoParent();
            }
        }
        Visit();

        foreach (ushort s in states)
        {
            LookaheadIterator? it = lang.LookaheadIterator(s);
            if (it is not null)
            {
                it.Dispose();
                return s;
            }
        }
        // Fall back to scanning the whole state space.
        for (ushort s = 0; s < lang.StateCount; s++)
        {
            LookaheadIterator? it = lang.LookaheadIterator(s);
            if (it is not null)
            {
                it.Dispose();
                return s;
            }
        }
        throw new InvalidOperationException("No valid lookahead state found.");
    }

    [Fact]
    public void Ctor_with_valid_state_then_iterate()
    {
        ushort state = ValidState();
        using var it = new LookaheadIterator(Grammars.Json, state);
        Assert.Same(Grammars.Json, it.Language);

        bool advanced = it.Next();
        Assert.True(advanced);
        Assert.True(it.CurrentSymbol >= 0);
        // The name may be null for some internal symbols, but for the first valid one
        // it is typically present.
        _ = it.CurrentSymbolName;
    }

    [Fact]
    public void Ctor_null_language_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new LookaheadIterator(null!, 0));
    }

    [Fact]
    public void Ctor_invalid_state_throws()
    {
        // Find an invalid state (one beyond the table) and assert the throwing ctor.
        ushort invalid = (ushort)Math.Min(Grammars.Json.StateCount + 1000, ushort.MaxValue);
        Assert.Throws<ArgumentException>(() => new LookaheadIterator(Grammars.Json, invalid));
    }

    [Fact]
    public void Symbols_enumerable_yields_symbols()
    {
        ushort state = ValidState();
        using var it = new LookaheadIterator(Grammars.Json, state);
        List<ushort> symbols = it.Symbols().ToList();
        Assert.NotEmpty(symbols);
    }

    [Fact]
    public void ResetState_in_same_language()
    {
        ushort state = ValidState();
        using var it = new LookaheadIterator(Grammars.Json, state);
        it.Next();
        bool ok = it.ResetState(state);
        Assert.True(ok);
    }

    [Fact]
    public void Reset_to_new_language_and_state()
    {
        ushort state = ValidState();
        using var it = new LookaheadIterator(Grammars.Json, state);
        it.Next();
        bool ok = it.Reset(Grammars.Json, state);
        Assert.True(ok);
        Assert.Same(Grammars.Json, it.Language);
    }

    [Fact]
    public void Reset_null_language_throws()
    {
        ushort state = ValidState();
        using var it = new LookaheadIterator(Grammars.Json, state);
        Assert.Throws<ArgumentNullException>(() => it.Reset(null!, state));
    }

    [Fact]
    public void Double_dispose_is_safe()
    {
        ushort state = ValidState();
        var it = new LookaheadIterator(Grammars.Json, state);
        it.Dispose();
        it.Dispose();
    }

    [Fact]
    public void Use_after_dispose_throws()
    {
        ushort state = ValidState();
        var it = new LookaheadIterator(Grammars.Json, state);
        it.Dispose();
        Assert.Throws<ObjectDisposedException>(() => it.Next());
    }
}
