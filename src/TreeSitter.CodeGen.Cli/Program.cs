using TreeSitter.CodeGen;

namespace TreeSitter.CodeGen.Cli;

/// <summary>
/// A thin command-line wrapper over <see cref="NodeTypesGenerator"/>. It reads a
/// grammar's <c>node-types.json</c> and writes the generated <c>.cs</c> file(s) to
/// an output directory.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || HasFlag(args, "--help", "-h"))
            {
                PrintUsage();
                return args.Length == 0 ? 1 : 0;
            }

            string? input = GetOption(args, "--input", "-i");
            string? ns = GetOption(args, "--namespace", "-n");
            string? language = GetOption(args, "--language", "-l");
            string? output = GetOption(args, "--output", "-o");

            var missing = new List<string>();
            if (input is null) missing.Add("--input");
            if (ns is null) missing.Add("--namespace");
            if (language is null) missing.Add("--language");
            if (output is null) missing.Add("--output");
            if (missing.Count > 0)
            {
                Console.Error.WriteLine($"error: missing required option(s): {string.Join(", ", missing)}");
                PrintUsage();
                return 1;
            }

            if (!File.Exists(input))
            {
                Console.Error.WriteLine($"error: input file not found: {input}");
                return 1;
            }

            string json = File.ReadAllText(input!);
            var options = new GeneratorOptions { RootNamespace = ns!, LanguageName = language! };
            GeneratedGrammar result = NodeTypesGenerator.Generate(json, options);

            Directory.CreateDirectory(output!);
            foreach (GeneratedFile file in result.Files)
            {
                string path = Path.Combine(output!, file.FileName);
                File.WriteAllText(path, file.Source);
                Console.WriteLine($"wrote {path}");
            }

            Console.WriteLine(
                $"generated {result.TotalTypeCount} types " +
                $"(concrete: {result.ConcreteNodeCount}, supertypes: {result.SupertypeCount}, " +
                $"unnamed: {result.UnnamedNodeCount}, unions: {result.AnonUnionCount}) " +
                $"for '{language}'.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            tsgen — generate strongly-typed tree-sitter node wrappers.

            Usage:
              tsgen --input <node-types.json> --namespace <ns> --language <name> --output <dir>

            Options:
              -i, --input      Path to the grammar's node-types.json (required).
              -n, --namespace  Root namespace for generated types (required).
              -l, --language   Grammar/language name, e.g. json, python (required).
              -o, --output     Output directory for generated .cs file(s) (required).
              -h, --help       Show this help.
            """);
    }

    private static string? GetOption(string[] args, string longName, string shortName)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a == longName || a == shortName)
                return i + 1 < args.Length ? args[i + 1] : null;
            // Support --name=value form.
            if (a.StartsWith(longName + "=", StringComparison.Ordinal))
                return a[(longName.Length + 1)..];
        }
        return null;
    }

    private static bool HasFlag(string[] args, params string[] names) =>
        args.Any(a => names.Contains(a));
}
