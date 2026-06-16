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
│          via the `tsgen` CLI. Runtime support lives in core.      │
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
  TreeSitter.CodeGen/         node-types.json -> C# (library)
  TreeSitter.CodeGen.Cli/     the `tsgen` CLI front end over the library
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

* One `internal static partial class NativeMethods` declaring the **full ABI-15
  surface** (the P/Invoke layer declares roughly **148** `ts_*` entry points).
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
  byte offsets from tree-sitter index directly into it. Text extraction slices
  the UTF-8 buffer and decodes on demand; convenience `string` overloads exist.
* `Node` is a **`readonly struct`** wrapping `TSNode` plus a reference to its
  owning `Tree` (so `Text`, `Language`, and symbol-name lookups work and the
  native tree is kept alive). `default(Node)`/null nodes are surfaced as
  `IsNull`. A node is only valid while its owning `Tree` is alive and undisposed:
  member calls go straight to native code with no per-call liveness guard, so
  using a node after `tree.Dispose()` is undefined (see the remarks on `Node`
  and `Tree.Dispose`).
* Disposable native owners (`Parser`, `Tree`, `Query`, `QueryCursor`,
  `TreeCursor`, `LookaheadIterator`) implement `IDisposable` with the standard
  safe-handle/finalizer pattern; double-dispose is safe. `QueryCursor` also roots
  the executing `Query` and owns the native options/deadline cells for the
  duration of a timed exec, since `ts_query_cursor_exec_with_options` retains the
  options pointer across subsequent `NextMatch`/`NextCapture` calls.
* All fallible operations have non-throwing `Try*` forms; query compilation
  surfaces `(offset, TSQueryError)` as a typed `QueryException`.

## Layer 2 — Strong typing (the core ask)

Mirrors `type-sitter` semantics in idiomatic C#. The generator consumes a
grammar's `src/node-types.json` and emits one `.cs` file of zero-allocation
`readonly struct` wrappers over `Node`.

* **Concrete named node** → `readonly partial struct Foo : ITypedNode<Foo>`
  wrapping a `Node`. The kind-checking constructor is **`private`** (it only
  assigns the node); the public construction surface is exactly two static
  factories:
  * `TryFrom(Node)` → `Foo?` — validates `Accepts(node.Kind)` (and non-null),
    returns `null` on mismatch.
  * `FromUnchecked(Node)` → `Foo` — the documented unchecked escape hatch for
    callers that already know the kind (e.g. dispatch after a `kind` switch); it
    carries a `Debug.Assert(Accepts(...))` but performs no check in Release.

  `default(Foo)` is a null-node value, exactly as for `Node`. This is the core
  safety invariant — *a non-default `Foo` guarantees the underlying node’s kind
  is accepted* — and it now holds in **all** build configurations, because the
  only validating path (`TryFrom`) is unconditional.
* **Field accessors** are generated from each field's `{required, multiple}`:

  | required | multiple | C# member |
  |----------|----------|-----------|
  | true     | false    | `T Name { get; }`        (throws `IncorrectNodeKindException` if the field child is **absent** or has an **unexpected** kind; a correctly-kinded **MISSING** node passes through) |
  | false    | false    | `T? Name { get; }`       (null when absent)        |
  | any      | true     | `IEnumerable<T> Name { get; }` (lazy, extras filtered) |

* **Supertype nodes** (`subtypes` in node-types.json) and **multi-type
  fields/children** (anonymous unions) are **zero-allocation `readonly struct`s**
  (not a sealed class hierarchy). Each exposes a nested `Variant` enum, a `Which`
  discriminator, `As<Variant>()` downcasts (each returns `Variant?` via
  `TryFrom`), and exhaustive `Match<TResult>(…)` / `Switch(…)` dispatchers
  (dispatch arms wrap via `FromUnchecked`, the kind already being known). Anon
  unions live in an `AnonUnions` namespace, named by joining member type names
  with `_`, hashed when the joined name would exceed 100 chars (matches
  type-sitter).
* **Identifier sanitization:** strip a single leading `_`; map punctuation via a
  fixed table (`+`→`Plus`, `<`→`Lt` so `<<`→`LtLt`, `*`→`Star`, …); any other
  non-identifier char → `U{hex}`; snake_case→PascalCase for types and members;
  a leading digit gets a `_` prefix; C# keywords and cross-name collisions get a
  trailing `_` suffix (never a `@` prefix). Sanitization is pure/deterministic so
  output is byte-stable across runs and machines.
* **Extra/Error/Missing/Untyped:** runtime types `ExtraNode`, `ErrorNode`,
  `MissingNode`, `UntypedNode`. `UntypedNode` imposes no kind constraint and
  offers `Is<T>()` (predicate), `As<T>()` (→ `T?`), and `Cast<T>()` (→ `T`,
  throwing `IncorrectNodeKindException` on mismatch). `ExtraNode`/`ErrorNode`/
  `MissingNode` are runtime-property wrappers (extra/error/missing-ness is not a
  kind), so their checked factory is a throwing `Wrap(Node)` (throws in all
  configs) alongside `TryFrom`/`FromUnchecked`; their kind-checking ctor is
  private. Extras are filtered out of child/field iterators.
* **Generation mechanism:** a reusable `TreeSitter.CodeGen` library emits source
  strings, fronted by the `dotnet tsgen` CLI, which writes inspectable `.cs` into
  `grammars/`. This is the implemented generation path. Generated `.cs` for the
  supported languages is checked in under `grammars/`, and a test asserts the
  generator reproduces it byte-for-byte.

### Roadmap / not yet implemented

* **Typed queries.** A generated typed-query surface (a `.scm`-backed wrapper
  whose capture accessors follow each capture's quantifier as `T` / `T?` /
  `IEnumerable<T>`) is planned but **not implemented** yet.
* **Roslyn incremental source generator.** A zero-wiring path that reads
  `node-types.json` from `AdditionalFiles` and generates the typed wrappers at
  compile time is planned but **not implemented**; today the `tsgen` CLI is the
  only generation path. There is no `TreeSitter.SourceGenerator` project.

## Layer 3 — Language pack

* Ports `language_definitions.json` (306 languages: `repo`, `rev`,
  `branch?`, `directory?`, `generate?`, `extensions`, `c_symbol?`,
  `abi_version?`) into an embedded manifest, parsed once into both the
  `name -> LanguageInfo` index and the extension index.
* `build-native.sh <c_symbol> <srcDir>` compiles `parser.c` (+ `scanner.c` as
  C11 / `scanner.cc` as C++ linked with the C++ runtime) into
  `native/<rid>/tree-sitter-<c_symbol>.so`; `scripts/fetch-grammar.sh <name>`
  clones the pinned `rev` (optionally running `tree-sitter generate`) and builds.
* `LanguagePack.Get("python")` resolves the `c_symbol`, loads the grammar lib
  via the resolver, invokes its `tree_sitter_<c_symbol>` export, and returns a
  cached `Language`. The pre-built grammar subset spans all scanner variants:
  `json`, `go`, `c` (no scanner), `python`, `rust`, `javascript` (C scanner),
  `cpp`, `bash` (C++ scanner).

## Testing & CI

* xUnit v3, `coverlet` line+branch coverage (collected in **Debug** — the Release
  interop thunks crash the collector), target ≥ 95% on `TreeSitter`,
  `TreeSitter.CodeGen`, and `TreeSitter.LanguagePack`. Native libs are built by
  `build-native.sh` / `scripts/fetch-test-grammars.sh` (also run from a
  SessionStart hook and a GitHub Actions workflow) before tests execute.
