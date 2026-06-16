# csharp-tree-sitter

Production-ready, cross-platform, strongly-typed C# bindings for
[tree-sitter](https://github.com/tree-sitter/tree-sitter) (ABI 15 / v0.26.x).

The repository ships several packages:

| Project | Package | Purpose |
|---|---|---|
| `src/TreeSitter` | `TreeSitter` | The core binding: `Parser`, `Tree`, `Node`, `Query`, `Language`, the typed-node layer, and the native-library resolver. Bundles only the tree-sitter **engine** (`libtree-sitter`). |
| `src/TreeSitter.LanguagePack` | `TreeSitter.LanguagePack` | Create parsers **by name**. Embeds the upstream [tree-sitter-language-pack](https://github.com/kreuzberg-dev/tree-sitter-language-pack) manifest (306 languages) and loads the matching native grammar. Bundles a subset of pre-built grammar libraries. |
| `src/TreeSitter.CodeGen[.Cli]` | (`tsgen`) | Generates strongly-typed node wrappers from a grammar's `node-types.json`. |
| `grammars/TreeSitter.Grammars.Json`, `…Python` | `TreeSitter.Grammars.*` | Worked examples of generated typed bindings. |

## Supported platforms

Native assets are built and bundled for **`linux-x64`** and **`osx-arm64`**. The
binding runs on any platform for which you build the native libraries; the resolver
also honours the standard NuGet `runtimes/<rid>/native/` layout.

## Building the native libraries

The managed code is pure .NET, but tree-sitter itself is native C. Build the engine and
grammars into `native/<rid>/` (e.g. `native/linux-x64/`):

```bash
# Core tree-sitter engine (from the tree-sitter/tree-sitter submodule):
build/build-native.sh

# A specific grammar from a source directory that contains parser.c (+ optional scanner):
build/build-native.sh <c_symbol> <srcDir>

# Or fetch + build any grammar by name from the bundled manifest (clones the pinned rev):
scripts/fetch-grammar.sh python ruby css            # selected languages
scripts/fetch-grammar.sh --all                      # every non-CLI grammar
scripts/fetch-grammar.sh --list                     # list all 306 manifest keys

# Build the core + the grammar subset the tests use, in one step:
scripts/fetch-test-grammars.sh
```

Grammars marked `generate: true` in the manifest require the `tree-sitter` CLI on
`PATH`; without it they are skipped with a clear warning (unless they already ship a
pre-generated `parser.c`).

When running locally, point the resolver at your build output:

```bash
export TREE_SITTER_NATIVE_PATH=$PWD/native/linux-x64
```

## Parsing with the object-oriented API

```csharp
using TreeSitter;

var language = new Language(tree_sitter_json()); // a const TSLanguage* from a grammar lib
using var parser = new Parser(language);
using Tree? tree = parser.Parse("""{ "hello": "world" }""");

Node root = tree!.RootNode;
Console.WriteLine(root.Kind);            // "document"
Console.WriteLine(root.ToSExpression()); // (document (object (pair ...)))
```

## Creating parsers by name with `LanguagePack`

`TreeSitter.LanguagePack` resolves a grammar from its language name and loads the native
library for you — no per-grammar P/Invoke required:

```csharp
using TreeSitter;
using TreeSitter.LanguagePack;

// Inspect the manifest.
bool ok = LanguagePack.IsDefined("python");                 // true
LanguageInfo info = LanguagePack.GetInfo("typescript");      // repo, rev, directory, c_symbol, ...
IReadOnlyCollection<string> all = LanguagePack.AvailableLanguages; // 306 names, sorted

// Resolve a language from a file extension.
string? lang = LanguagePack.FindByExtension(".py");          // "python"

// Load a Language (cached) or create a ready-to-use Parser.
Language python = LanguagePack.Get("python");
using Parser parser = LanguagePack.CreateParser("python");
using Tree? tree = parser.Parse("def hello(): pass");
Console.WriteLine(tree!.RootNode.Kind);                      // "module"

// Non-throwing variants.
if (LanguagePack.TryGet("rust", out Language? rust)) { /* ... */ }
```

If a language is defined in the manifest but its native library has not been built, `Get`
throws `LanguageNotAvailableException` explaining how to build it (and `TryGet` returns
`false`).

## Strongly-typed trees with `tsgen`

Generate typed node wrappers from a grammar's `node-types.json`:

```bash
dotnet run --project src/TreeSitter.CodeGen.Cli -- \
  --input  /path/to/<grammar>/src/node-types.json \
  --namespace TreeSitter.Grammars.Python \
  --language  python \
  --output    grammars/TreeSitter.Grammars.Python
```

The generated structs are zero-allocation wrappers over `Node` with typed field
accessors. The `TreeSitter.Grammars.Python` project is a checked-in example that resolves
its grammar via `LanguagePack`:

```csharp
using TreeSitter;
using TreeSitter.Grammars.Python;

using Parser parser = PythonLanguage.CreateParser();
using Tree? tree = parser.Parse("def greet(name):\n    return name\n");

Module module = Module.TryFrom(tree!.RootNode)!.Value;
FunctionDefinition func = module.Children
    .Select(c => FunctionDefinition.TryFrom(c.Node))
    .First(f => f is not null)!.Value;

Console.WriteLine(func.Name.Node.Text); // "greet"
```

## Building & testing

```bash
export TREE_SITTER_NATIVE_PATH=$PWD/native/linux-x64
dotnet build TreeSitter.slnx -c Debug
dotnet test  TreeSitter.slnx
```

## License

MIT. The bundled `language_definitions.json` manifest data is from
tree-sitter-language-pack (MIT); see `src/TreeSitter.LanguagePack/NOTICE`. Individual
grammars are owned by their upstream authors under their own licenses.
