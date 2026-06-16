using System.Runtime.InteropServices;

namespace TreeSitter.Native;

// =============================================================================
// Layer 0 -- complete P/Invoke surface for tree-sitter (v0.26.9, ABI 15).
//
// Every public function declared in lib/include/tree_sitter/api.h is bound here
// (86 logical API functions; the WebAssembly + global-allocator entry points are
// included for completeness). Signatures are the source-of-truth from the header.
//
// Conventions:
//   * Logical library name is "tree-sitter"; the NativeLibrary resolver maps it
//     to the platform file (libtree-sitter.so / tree-sitter.dll / .dylib).
//   * [LibraryImport] (source-generated marshalling) is used everywhere -- all
//     of these signatures are blittable once strings are marshalled as UTF-8 and
//     C function pointers are expressed as `delegate* unmanaged[Cdecl]<...>`.
//   * `bool` returns/params use [MarshalAs(UnmanagedType.U1)] to match C `bool`.
//   * TSNode and the other small structs are passed BY VALUE.
//   * The C ABI is cdecl; on the platforms we target the source-generated stubs
//     use the platform default which is cdecl for these RIDs.
// =============================================================================

internal static unsafe partial class NativeMethods
{
    private const string Lib = "tree-sitter";

    // ---------------------------------------------------------------------
    // Section - Parser (12)
    // ---------------------------------------------------------------------

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_parser_new();

    [LibraryImport(Lib)]
    internal static partial void ts_parser_delete(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_parser_language(IntPtr self);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_parser_set_language(IntPtr self, IntPtr language);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_parser_set_included_ranges(IntPtr self, TSRange* ranges, uint count);

    [LibraryImport(Lib)]
    internal static partial TSRange* ts_parser_included_ranges(IntPtr self, uint* count);

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_parser_parse(IntPtr self, IntPtr oldTree, TSInput input);

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_parser_parse_with_options(IntPtr self, IntPtr oldTree, TSInput input, TSParseOptions parseOptions);

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_parser_parse_string(IntPtr self, IntPtr oldTree, byte* str, uint length);

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_parser_parse_string_encoding(IntPtr self, IntPtr oldTree, byte* str, uint length, int encoding);

    [LibraryImport(Lib)]
    internal static partial void ts_parser_reset(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial void ts_parser_set_logger(IntPtr self, TSLogger logger);

    [LibraryImport(Lib)]
    internal static partial TSLogger ts_parser_logger(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial void ts_parser_print_dot_graphs(IntPtr self, int fd);

    // ---------------------------------------------------------------------
    // Section - Tree (8)
    // ---------------------------------------------------------------------

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_tree_copy(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial void ts_tree_delete(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial TSNode ts_tree_root_node(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial TSNode ts_tree_root_node_with_offset(IntPtr self, uint offsetBytes, TSPoint offsetExtent);

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_tree_language(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial TSRange* ts_tree_included_ranges(IntPtr self, uint* length);

    [LibraryImport(Lib)]
    internal static partial void ts_tree_edit(IntPtr self, TSInputEdit* edit);

    [LibraryImport(Lib)]
    internal static partial TSRange* ts_tree_get_changed_ranges(IntPtr oldTree, IntPtr newTree, uint* length);

    [LibraryImport(Lib)]
    internal static partial void ts_tree_print_dot_graph(IntPtr self, int fileDescriptor);

    // ---------------------------------------------------------------------
    // Section - Node (33)
    // ---------------------------------------------------------------------

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_node_type(TSNode self);

    [LibraryImport(Lib)]
    internal static partial ushort ts_node_symbol(TSNode self);

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_node_language(TSNode self);

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_node_grammar_type(TSNode self);

    [LibraryImport(Lib)]
    internal static partial ushort ts_node_grammar_symbol(TSNode self);

    [LibraryImport(Lib)]
    internal static partial uint ts_node_start_byte(TSNode self);

    [LibraryImport(Lib)]
    internal static partial TSPoint ts_node_start_point(TSNode self);

    [LibraryImport(Lib)]
    internal static partial uint ts_node_end_byte(TSNode self);

    [LibraryImport(Lib)]
    internal static partial TSPoint ts_node_end_point(TSNode self);

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_node_string(TSNode self);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_node_is_null(TSNode self);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_node_is_named(TSNode self);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_node_is_missing(TSNode self);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_node_is_extra(TSNode self);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_node_has_changes(TSNode self);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_node_has_error(TSNode self);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_node_is_error(TSNode self);

    [LibraryImport(Lib)]
    internal static partial ushort ts_node_parse_state(TSNode self);

    [LibraryImport(Lib)]
    internal static partial ushort ts_node_next_parse_state(TSNode self);

    [LibraryImport(Lib)]
    internal static partial TSNode ts_node_parent(TSNode self);

    [LibraryImport(Lib)]
    internal static partial TSNode ts_node_child_with_descendant(TSNode self, TSNode descendant);

    [LibraryImport(Lib)]
    internal static partial TSNode ts_node_child(TSNode self, uint childIndex);

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_node_field_name_for_child(TSNode self, uint childIndex);

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_node_field_name_for_named_child(TSNode self, uint namedChildIndex);

    [LibraryImport(Lib)]
    internal static partial uint ts_node_child_count(TSNode self);

    [LibraryImport(Lib)]
    internal static partial TSNode ts_node_named_child(TSNode self, uint childIndex);

    [LibraryImport(Lib)]
    internal static partial uint ts_node_named_child_count(TSNode self);

    [LibraryImport(Lib)]
    internal static partial TSNode ts_node_child_by_field_name(TSNode self, byte* name, uint nameLength);

    [LibraryImport(Lib)]
    internal static partial TSNode ts_node_child_by_field_id(TSNode self, ushort fieldId);

    [LibraryImport(Lib)]
    internal static partial TSNode ts_node_next_sibling(TSNode self);

    [LibraryImport(Lib)]
    internal static partial TSNode ts_node_prev_sibling(TSNode self);

    [LibraryImport(Lib)]
    internal static partial TSNode ts_node_next_named_sibling(TSNode self);

    [LibraryImport(Lib)]
    internal static partial TSNode ts_node_prev_named_sibling(TSNode self);

    [LibraryImport(Lib)]
    internal static partial TSNode ts_node_first_child_for_byte(TSNode self, uint @byte);

    [LibraryImport(Lib)]
    internal static partial TSNode ts_node_first_named_child_for_byte(TSNode self, uint @byte);

    [LibraryImport(Lib)]
    internal static partial uint ts_node_descendant_count(TSNode self);

    [LibraryImport(Lib)]
    internal static partial TSNode ts_node_descendant_for_byte_range(TSNode self, uint start, uint end);

    [LibraryImport(Lib)]
    internal static partial TSNode ts_node_descendant_for_point_range(TSNode self, TSPoint start, TSPoint end);

    [LibraryImport(Lib)]
    internal static partial TSNode ts_node_named_descendant_for_byte_range(TSNode self, uint start, uint end);

    [LibraryImport(Lib)]
    internal static partial TSNode ts_node_named_descendant_for_point_range(TSNode self, TSPoint start, TSPoint end);

    [LibraryImport(Lib)]
    internal static partial void ts_node_edit(TSNode* self, TSInputEdit* edit);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_node_eq(TSNode self, TSNode other);

    [LibraryImport(Lib)]
    internal static partial void ts_point_edit(TSPoint* point, uint* pointByte, TSInputEdit* edit);

    [LibraryImport(Lib)]
    internal static partial void ts_range_edit(TSRange* range, TSInputEdit* edit);

    // ---------------------------------------------------------------------
    // Section - TreeCursor (15)
    // ---------------------------------------------------------------------

    [LibraryImport(Lib)]
    internal static partial TSTreeCursor ts_tree_cursor_new(TSNode node);

    [LibraryImport(Lib)]
    internal static partial void ts_tree_cursor_delete(TSTreeCursor* self);

    [LibraryImport(Lib)]
    internal static partial void ts_tree_cursor_reset(TSTreeCursor* self, TSNode node);

    [LibraryImport(Lib)]
    internal static partial void ts_tree_cursor_reset_to(TSTreeCursor* dst, TSTreeCursor* src);

    [LibraryImport(Lib)]
    internal static partial TSNode ts_tree_cursor_current_node(TSTreeCursor* self);

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_tree_cursor_current_field_name(TSTreeCursor* self);

    [LibraryImport(Lib)]
    internal static partial ushort ts_tree_cursor_current_field_id(TSTreeCursor* self);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_tree_cursor_goto_parent(TSTreeCursor* self);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_tree_cursor_goto_next_sibling(TSTreeCursor* self);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_tree_cursor_goto_previous_sibling(TSTreeCursor* self);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_tree_cursor_goto_first_child(TSTreeCursor* self);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_tree_cursor_goto_last_child(TSTreeCursor* self);

    [LibraryImport(Lib)]
    internal static partial void ts_tree_cursor_goto_descendant(TSTreeCursor* self, uint goalDescendantIndex);

    [LibraryImport(Lib)]
    internal static partial uint ts_tree_cursor_current_descendant_index(TSTreeCursor* self);

    [LibraryImport(Lib)]
    internal static partial uint ts_tree_cursor_current_depth(TSTreeCursor* self);

    [LibraryImport(Lib)]
    internal static partial long ts_tree_cursor_goto_first_child_for_byte(TSTreeCursor* self, uint goalByte);

    [LibraryImport(Lib)]
    internal static partial long ts_tree_cursor_goto_first_child_for_point(TSTreeCursor* self, TSPoint goalPoint);

    [LibraryImport(Lib)]
    internal static partial TSTreeCursor ts_tree_cursor_copy(TSTreeCursor* cursor);

    // ---------------------------------------------------------------------
    // Section - Query (14)
    // ---------------------------------------------------------------------

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_query_new(IntPtr language, byte* source, uint sourceLen, uint* errorOffset, int* errorType);

    [LibraryImport(Lib)]
    internal static partial void ts_query_delete(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial uint ts_query_pattern_count(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial uint ts_query_capture_count(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial uint ts_query_string_count(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial uint ts_query_start_byte_for_pattern(IntPtr self, uint patternIndex);

    [LibraryImport(Lib)]
    internal static partial uint ts_query_end_byte_for_pattern(IntPtr self, uint patternIndex);

    [LibraryImport(Lib)]
    internal static partial TSQueryPredicateStep* ts_query_predicates_for_pattern(IntPtr self, uint patternIndex, uint* stepCount);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_query_is_pattern_rooted(IntPtr self, uint patternIndex);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_query_is_pattern_non_local(IntPtr self, uint patternIndex);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_query_is_pattern_guaranteed_at_step(IntPtr self, uint byteOffset);

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_query_capture_name_for_id(IntPtr self, uint index, uint* length);

    [LibraryImport(Lib)]
    internal static partial int ts_query_capture_quantifier_for_id(IntPtr self, uint patternIndex, uint captureIndex);

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_query_string_value_for_id(IntPtr self, uint index, uint* length);

    [LibraryImport(Lib)]
    internal static partial void ts_query_disable_capture(IntPtr self, byte* name, uint length);

    [LibraryImport(Lib)]
    internal static partial void ts_query_disable_pattern(IntPtr self, uint patternIndex);

    // ---------------------------------------------------------------------
    // Section - QueryCursor (14)
    // ---------------------------------------------------------------------

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_query_cursor_new();

    [LibraryImport(Lib)]
    internal static partial void ts_query_cursor_delete(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial void ts_query_cursor_exec(IntPtr self, IntPtr query, TSNode node);

    [LibraryImport(Lib)]
    internal static partial void ts_query_cursor_exec_with_options(IntPtr self, IntPtr query, TSNode node, TSQueryCursorOptions* queryOptions);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_query_cursor_did_exceed_match_limit(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial uint ts_query_cursor_match_limit(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial void ts_query_cursor_set_match_limit(IntPtr self, uint limit);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_query_cursor_set_byte_range(IntPtr self, uint startByte, uint endByte);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_query_cursor_set_point_range(IntPtr self, TSPoint startPoint, TSPoint endPoint);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_query_cursor_set_containing_byte_range(IntPtr self, uint startByte, uint endByte);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_query_cursor_set_containing_point_range(IntPtr self, TSPoint startPoint, TSPoint endPoint);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_query_cursor_next_match(IntPtr self, TSQueryMatch* match);

    [LibraryImport(Lib)]
    internal static partial void ts_query_cursor_remove_match(IntPtr self, uint matchId);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_query_cursor_next_capture(IntPtr self, TSQueryMatch* match, uint* captureIndex);

    [LibraryImport(Lib)]
    internal static partial void ts_query_cursor_set_max_start_depth(IntPtr self, uint maxStartDepth);

    // ---------------------------------------------------------------------
    // Section - Language (15)
    // ---------------------------------------------------------------------

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_language_copy(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial void ts_language_delete(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial uint ts_language_symbol_count(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial uint ts_language_state_count(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial ushort ts_language_symbol_for_name(IntPtr self, byte* str, uint length, [MarshalAs(UnmanagedType.U1)] bool isNamed);

    [LibraryImport(Lib)]
    internal static partial uint ts_language_field_count(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_language_field_name_for_id(IntPtr self, ushort id);

    [LibraryImport(Lib)]
    internal static partial ushort ts_language_field_id_for_name(IntPtr self, byte* name, uint nameLength);

    [LibraryImport(Lib)]
    internal static partial ushort* ts_language_supertypes(IntPtr self, uint* length);

    [LibraryImport(Lib)]
    internal static partial ushort* ts_language_subtypes(IntPtr self, ushort supertype, uint* length);

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_language_symbol_name(IntPtr self, ushort symbol);

    [LibraryImport(Lib)]
    internal static partial int ts_language_symbol_type(IntPtr self, ushort symbol);

    [LibraryImport(Lib)]
    internal static partial uint ts_language_abi_version(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial TSLanguageMetadata* ts_language_metadata(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial ushort ts_language_next_state(IntPtr self, ushort state, ushort symbol);

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_language_name(IntPtr self);

    // ---------------------------------------------------------------------
    // Section - LookaheadIterator (8)
    // ---------------------------------------------------------------------

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_lookahead_iterator_new(IntPtr self, ushort state);

    [LibraryImport(Lib)]
    internal static partial void ts_lookahead_iterator_delete(IntPtr self);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_lookahead_iterator_reset_state(IntPtr self, ushort state);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_lookahead_iterator_reset(IntPtr self, IntPtr language, ushort state);

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_lookahead_iterator_language(IntPtr self);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_lookahead_iterator_next(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial ushort ts_lookahead_iterator_current_symbol(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_lookahead_iterator_current_symbol_name(IntPtr self);

    // ---------------------------------------------------------------------
    // Section - WebAssembly Integration (5 store/language-side + parser glue)
    //
    // These are bound for completeness. The TSWasmEngine handle is opaque and a
    // managed wasmtime engine is out of scope for Layers 0/1; callers supplying
    // their own engine pointer can still use these.
    // ---------------------------------------------------------------------

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_wasm_store_new(IntPtr engine, IntPtr error);

    [LibraryImport(Lib)]
    internal static partial void ts_wasm_store_delete(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_wasm_store_load_language(IntPtr self, byte* name, byte* wasm, uint wasmLen, IntPtr error);

    [LibraryImport(Lib)]
    internal static partial nuint ts_wasm_store_language_count(IntPtr self);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ts_language_is_wasm(IntPtr self);

    [LibraryImport(Lib)]
    internal static partial void ts_parser_set_wasm_store(IntPtr self, IntPtr store);

    [LibraryImport(Lib)]
    internal static partial IntPtr ts_parser_take_wasm_store(IntPtr self);

    // ---------------------------------------------------------------------
    // Section - Global Configuration (1)
    // ---------------------------------------------------------------------

    [LibraryImport(Lib)]
    internal static partial void ts_set_allocator(
        delegate* unmanaged[Cdecl]<nuint, IntPtr> newMalloc,
        delegate* unmanaged[Cdecl]<nuint, nuint, IntPtr> newCalloc,
        delegate* unmanaged[Cdecl]<IntPtr, nuint, IntPtr> newRealloc,
        delegate* unmanaged[Cdecl]<IntPtr, void> newFree);
}
