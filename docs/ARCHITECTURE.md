# csharp-tree-sitter — Architecture

Production-ready, cross-platform, **strongly-typed** C# bindings for
[tree-sitter](https://github.com/tree-sitter/tree-sitter) (pinned to the latest
stable release, **v0.26.9**, language ABI **15**, min-compatible **13**).

The design is layered so each layer has a single responsibility and can be
tested in isolation. Higher layers are built strictly on top of lower ones.

```
┌─────────────────────────────────────────────────────────────────┐
│ Layer 3  TreeSitter.LanguagePack  — get a Language by name,       │
│          build/load grammars from a ported manifest               │
├─────────────────────────────────────────────────────────────────┤
│ Layer 2  Strong-typed nodes (generated) + TreeSitter.CodeGen      │
│          node-types.json  ──►  typed node structs / supertypes    │
│          + typed queries. Runtime support lives in core.          │
├─────────────────────────────────────────────────────────────────┤
│ Layer 1  Safe OO API (public, hand-written, always available):    │
│          Parser, Tree, Node, TreeCursor, Query, QueryCursor,      │
│          Language, LookaheadIterator + value types + enums +      │
│          exceptions. IDisposable, nullable-annotated, UTF-8.      │
├─────────────────────────────────────────────────────────────────┤
│ Layer 0  Native interop (internal): complete ABI-15 P/Invoke,     │
│          blittable structs/enums, cross-platform NativeLibrary    │
│          resolver.                                                │
└─────────────────────────────────────────────────────────────────┘
                          libtree-sitter (C)  +  per-language grammars
```

## Solution layout

```
TreeSitter.slnx
src/
  TreeSitter/                 Layer 0 + 1 + typed runtime support (the package)
  TreeSitter.CodeGen/         node-types.json -> C# (library + `dotnet tsgen` CLI)
  TreeSitter.SourceGenerator/ Roslyn incremental generator wrapping CodeGen
  TreeSitter.LanguagePack/    Layer 3: manifest + fetch/build/load by name
tests/
  TreeSitter.Tests/           xUnit v3, coverage target >= 95%
build/
  build-native.sh / .ps1      build libtree-sitter + grammars -> native/<rid>
grammars/                     generated typed bindings (checked in, inspectable)
native/<rid>/                 built native outputs (git-ignored)
tree-sitter/tree-sitter       submodule, pinned v0.26.9
```

## Layer 0 — Native interop

* One `internal static partial class Native` with **all 86** ABI-15 functions.
  Prefer `[LibraryImport]` (source-generated, AOT-friendly) for blittable
  signatures; fall back to `[DllImport]` only where custom marshalling is needed.
* Logical native name is **`tree-sitter`** (no extension). A module initializer
  installs a `NativeLibrary.SetDllImportResolver` that maps logical names to
  `libtree-sitter.so` / `tree-sitter.dll` / `libtree-sitter.dylib` and searches,
  in order: `$TREE_SITTER_NATIVE_PATH`, `runtimes/<rid>/native/`, the app base
  dir, and the repo `native/<rid>/` dir (dev). Same resolver handles grammar
  libs (`tree-sitter-<lang>`).
* Blittable structs mirror the C layout exactly: `TSNode` (uint32×4 + 2 ptrs =
  32 bytes), `TSPoint`, `TSRange`, `TSInputEdit`, `TSQueryCapture`,
  `TSQueryMatch`, `TSQueryPredicateStep`, `TSInput`, `TSParseOptions`,
  `TSQueryCursorOptions`, `TSLanguageMetadata`, `TSLogger`. Nodes are passed
  **by value**.
* Memory ownership: strings from `ts_node_string` and arrays from
  `ts_tree_included_ranges` / `ts_tree_get_changed_ranges` must be released with
  libc `free` (P/Invoked separately), never the GC/Marshal heap.

## Layer 1 — Safe OO API

* **UTF-8 is the canonical encoding.** Source is held as `byte[]`/`ReadOnlyMemory<byte>`;
  byte offsets from tree-sitter index directly into it. (The legacy binding's
  `* sizeof(ushort)` UTF-16 offset arithmetic is removed.) Text extraction slices
  the UTF-8 buffer and decodes on demand; convenience `string` overloads exist.
* `Node` is a **`readonly struct`** wrapping `TSNode` plus a reference to its
  owning `Tree` (so `Text`, `Language`, and symbol-name lookups work and the
  native tree is kept alive). `default(Node)`/null nodes are surfaced as
  `IsNull`.
* Disposable native owners (`Parser`, `Tree`, `Query`, `QueryCursor`,
  `TreeCursor`, `LookaheadIterator`) implement `IDisposable` with the standard
  safe-handle/finalizer pattern; double-dispose is safe.
* All fallible operations have non-throwing `Try*` forms; query compilation
  surfaces `(offset, TSQueryError)` as a typed `QueryException`.

## Layer 2 — Strong typing (the core ask)

Mirrors `type-sitter` semantics in idiomatic C#. The generator consumes a
grammar's `src/node-types.json`.

* **Concrete named node** → `readonly struct Foo : ITypedNode` wrapping a `Node`.
  The only way to construct one is `TryCreate(Node)` (validates
  `node.Kind == "foo"`) or an `internal CreateUnchecked` used by dispatch code
  after a `kind` switch (with a `Debug.Assert`). This is the core safety
  invariant: *a `Foo` value guarantees the underlying node’s kind is `foo`.*
* **Field accessors** are generated from each field's `{required, multiple}`:

  | required | multiple | C# member |
  |----------|----------|-----------|
  | true     | false    | `T Name { get; }`        (throws on ERROR/MISSING) |
  | false    | false    | `T? Name { get; }`       (null when absent)        |
  | any      | true     | `IEnumerable<T> Name { get; }` (lazy, extras filtered) |

* **Supertype nodes** (`subtypes` in node-types.json) → a sealed class hierarchy
  (abstract base + sealed variants) or an enum-like discriminated wrapper with
  `As<Variant>()` downcasts and a `kind`-`switch` `TryCreate`.
* **Multi-type fields/children** → generated **anonymous-union** types in an
  `AnonUnions` namespace, named by joining member type names with `_`, hashed
  when the joined name would exceed 100 chars (matches type-sitter).
* **Identifier sanitization:** strip leading `_`; map punctuation via a fixed
  table (`+`→`Add`, `<<`→`LtLt`, …); non-identifier chars → `U{hex}`;
  snake_case→PascalCase for types, snake_case methods; C# keywords get a `@`
  prefix; leading digit gets `_`; deduplicate deterministically with `_` suffix.
* **Extra/Error/Missing/Untyped:** runtime types `ExtraNode`, `ErrorNode`,
  `MissingNode`, `UntypedNode` (with `Is<T>()`/`TryCast<T>()`). Extras are
  filtered out of child/field iterators.
* **Typed queries:** a generated `sealed class XQuery : ITypedQuery` embeds the
  `.scm` text, lazily compiles a `Query`, and exposes capture accessors whose
  return shape follows the capture quantifier (`T` / `T?` / `IEnumerable<T>`).
* **Generation mechanism:** a reusable `TreeSitter.CodeGen` library emits source
  strings. It is fronted by (a) a `dotnet tsgen` CLI (writes inspectable `.cs`
  into `grammars/`, the primary path) and (b) a Roslyn incremental source
  generator reading `node-types.json` from `AdditionalFiles` (zero-wiring path).
  Generated `.cs` for the supported languages is checked in under `grammars/`.

## Layer 3 — Language pack

* Ports `sources/language_definitions.json` (306 languages: `repo`, `rev`,
  `branch?`, `directory?`, `generate?`, `extensions`, `c_symbol?`,
  `abi_version?`) into an embedded manifest.
* `build-native.sh <lang>` clones the pinned `rev`, optionally runs
  `tree-sitter generate`, compiles `parser.c` (+ `scanner.c` as C11 / `scanner.cc`
  as C++ linked with the C++ runtime) into `native/<rid>/tree-sitter-<c_symbol>.so`.
* `LanguagePack.Get("python")` resolves the `c_symbol`, loads the grammar lib
  via the resolver, and returns a `Language`. Initial supported subset spans all
  scanner variants: `json`, `go`, `c` (none), `python`, `rust`, `javascript`
  (C scanner), `cpp`, `bash` (C++ scanner).

## Testing & CI

* xUnit v3, `coverlet` line+branch coverage, target ≥ 95% on `TreeSitter` and
  `TreeSitter.CodeGen`. Native libs are built by `build-native.sh` (also run from
  a SessionStart hook and a GitHub Actions workflow) before tests execute.
</content>
