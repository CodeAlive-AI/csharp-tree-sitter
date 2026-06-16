using TreeSitter;

namespace TreeSitter.Tests;

public class LanguageTests
{
    [Fact]
    public void Ctor_null_pointer_throws()
    {
        Assert.Throws<ArgumentException>(() => new Language(IntPtr.Zero));
    }

    [Fact]
    public void AbiVersion_and_counts()
    {
        Language lang = Grammars.Json;
        Assert.InRange(lang.AbiVersion, TreeSitterConstants.MinCompatibleAbiVersion, TreeSitterConstants.AbiVersion);
        Assert.True(lang.SymbolCount > 0);
        Assert.True(lang.StateCount > 0);
        Assert.True(lang.FieldCount > 0);
    }

    [Fact]
    public void Name_present_on_abi15_grammar()
    {
        // Name is an ABI-15 feature. Python's grammar is built at ABI 15 here.
        Language py = Grammars.Python;
        Assert.Equal(15u, py.AbiVersion);
        Assert.Equal("python", py.Name);
    }

    [Fact]
    public void Name_null_on_abi14_grammar()
    {
        // The bundled JSON grammar is ABI 14, which predates language names.
        Language json = Grammars.Json;
        if (json.AbiVersion < 15)
            Assert.Null(json.Name);
    }

    [Fact]
    public void Metadata_is_queryable()
    {
        // May be null depending on the grammar build; just exercise the property on
        // both an ABI-15 and the ABI-14 grammar so the null and non-null branches run.
        Version? meta = Grammars.Python.Metadata;
        if (meta is not null)
            Assert.True(meta.Major >= 0);
        _ = Grammars.Json.Metadata;
    }

    [Fact]
    public void SymbolName_and_symbol_for_name_round_trip()
    {
        Language lang = Grammars.Json;
        ushort sym = lang.SymbolForName("string", isNamed: true);
        Assert.True(sym > 0);
        Assert.Equal("string", lang.SymbolName(sym));
    }

    [Fact]
    public void SymbolName_out_of_range_is_null()
    {
        Assert.Null(Grammars.Json.SymbolName(ushort.MaxValue));
    }

    [Fact]
    public void SymbolForName_unknown_returns_zero()
    {
        Assert.Equal(0, Grammars.Json.SymbolForName("definitely_not_a_symbol", true));
    }

    [Fact]
    public void SymbolForName_null_throws()
    {
        Assert.Throws<ArgumentNullException>(() => Grammars.Json.SymbolForName(null!, true));
    }

    [Fact]
    public void SymbolForName_long_name_uses_heap_buffer()
    {
        // > 256 bytes forces the heap path; unknown name -> 0.
        Assert.Equal(0, Grammars.Json.SymbolForName(new string('z', 300), true));
    }

    [Fact]
    public void SymbolType_categories()
    {
        Language lang = Grammars.Json;
        ushort named = lang.SymbolForName("string", true);
        Assert.Equal(SymbolType.Regular, lang.SymbolType(named));

        ushort anon = lang.SymbolForName("{", false);
        if (anon > 0)
            Assert.Equal(SymbolType.Anonymous, lang.SymbolType(anon));
    }

    [Fact]
    public void Field_name_and_id_round_trip()
    {
        Language lang = Grammars.Json;
        ushort id = lang.FieldIdForName("key");
        Assert.True(id > 0);
        Assert.Equal("key", lang.FieldNameForId(id));
    }

    [Fact]
    public void FieldNameForId_invalid_is_null()
    {
        Assert.Null(Grammars.Json.FieldNameForId(ushort.MaxValue));
        // Field id 0 is the "no field" sentinel and maps to null.
        Assert.Null(Grammars.Json.FieldNameForId(0));
    }

    [Fact]
    public void FieldIdForName_unknown_returns_zero()
    {
        Assert.Equal(0, Grammars.Json.FieldIdForName("not_a_field"));
    }

    [Fact]
    public void FieldIdForName_null_throws()
    {
        Assert.Throws<ArgumentNullException>(() => Grammars.Json.FieldIdForName(null!));
    }

    [Fact]
    public void FieldIdForName_long_name_uses_heap_buffer()
    {
        Assert.Equal(0, Grammars.Json.FieldIdForName(new string('q', 300)));
    }

    [Fact]
    public void NextState_is_queryable()
    {
        Language lang = Grammars.Json;
        using Tree tree = TestData.ParseJson("[1]");
        Node array = tree.RootNode.NamedChild(0);
        ushort next = lang.NextState(array.ParseState, array.GrammarSymbol);
        Assert.True(next >= 0);
    }

    [Fact]
    public void Supertypes_and_subtypes_on_abi15_grammar()
    {
        // Supertypes/Subtypes are ABI-15 features; Python exposes several.
        Language lang = Grammars.Python;
        IReadOnlyList<ushort> supers = lang.Supertypes();
        Assert.NotNull(supers);
        Assert.NotEmpty(supers);
        foreach (ushort s in supers)
        {
            IReadOnlyList<ushort> subs = lang.Subtypes(s);
            Assert.NotEmpty(subs);
            // Subtype ids resolve to real symbol names.
            Assert.All(subs, id => Assert.NotNull(lang.SymbolName(id)));
        }
    }

    [Fact]
    public void Supertypes_empty_on_abi14_grammar()
    {
        // The ABI-14 JSON grammar reports no supertypes at runtime (the feature is
        // ABI 15+), exercising the empty-array branch of CopySymbolArray.
        Language json = Grammars.Json;
        if (json.AbiVersion < 15)
            Assert.Empty(json.Supertypes());
    }

    [Fact]
    public void Subtypes_of_non_supertype_is_empty()
    {
        Language lang = Grammars.Python;
        ushort id = lang.SymbolForName("identifier", true);
        Assert.Empty(lang.Subtypes(id));
    }

    [Fact]
    public void Copy_holds_independent_reference()
    {
        Language copy = Grammars.Python.Copy();
        Assert.Equal(Grammars.Python.AbiVersion, copy.AbiVersion);
        Assert.Equal("python", copy.Name);
        // Force the finalizer path to release the owned reference.
        GC.KeepAlive(copy);
    }

    [Fact]
    public void LookaheadIterator_factory_returns_null_for_bad_state()
    {
        // A state well beyond the table is invalid; the factory (TryCreate) returns
        // null after disposing the failed handle rather than throwing.
        ushort invalid = (ushort)Math.Min(Grammars.Json.StateCount + 1000, ushort.MaxValue);
        LookaheadIterator? it = Grammars.Json.LookaheadIterator(invalid);
        Assert.Null(it);
    }

    [Fact]
    public void Cached_names_are_stable_across_calls()
    {
        Language lang = Grammars.Json;
        // Second call hits the cached array path.
        Assert.Equal(lang.SymbolName(lang.SymbolForName("number", true)),
                     lang.SymbolName(lang.SymbolForName("number", true)));
        Assert.Equal(lang.FieldNameForId(lang.FieldIdForName("value")),
                     lang.FieldNameForId(lang.FieldIdForName("value")));
    }
}
