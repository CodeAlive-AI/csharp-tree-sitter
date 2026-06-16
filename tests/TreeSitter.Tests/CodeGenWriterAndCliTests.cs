using System.Diagnostics;
using TreeSitter.CodeGen.Emit;

namespace TreeSitter.Tests;

public class CodeWriterTests
{
    [Fact]
    public void Line_blank_and_indent()
    {
        var w = new CodeWriter();
        w.Line("a");
        w.Indent();
        w.Line("b");
        w.Outdent();
        w.Line("c");
        Assert.Equal("a\n    b\nc\n", w.ToString());
    }

    [Fact]
    public void Empty_line_has_no_indentation()
    {
        var w = new CodeWriter();
        w.Indent();
        w.Line(""); // empty line: just a newline, no indent
        w.Blank();
        Assert.Equal("\n\n", w.ToString());
    }

    [Fact]
    public void OpenBrace_and_CloseBrace()
    {
        var w = new CodeWriter();
        w.OpenBrace("if (x)");
        w.Line("body;");
        w.CloseBrace();
        Assert.Equal("if (x)\n{\n    body;\n}\n", w.ToString());
    }

    [Fact]
    public void Outdent_never_goes_below_zero()
    {
        var w = new CodeWriter();
        w.Outdent().Outdent();
        w.Line("x");
        Assert.Equal("x\n", w.ToString());
    }

    [Fact]
    public void Block_indents_each_line()
    {
        var w = new CodeWriter();
        w.Indent();
        w.Block("one\ntwo");
        Assert.Equal("    one\n    two\n", w.ToString());
    }

    [Fact]
    public void Fluent_chaining_returns_self()
    {
        var w = new CodeWriter();
        CodeWriter same = w.Line("a").Blank().Indent().Outdent();
        Assert.Same(w, same);
    }
}

/// <summary>
/// Integration tests for the <c>tsgen</c> CLI. They run the built executable as a
/// subprocess so the entire <c>Program.Main</c> path (argument parsing, file IO,
/// error handling, usage text) is exercised.
/// </summary>
public class TsgenCliTests
{
    private static string TsgenDll()
    {
        // The CLI is project-referenced; its output sits beside the test assembly's
        // bin tree. Walk to the repo root and locate the built tsgen.dll.
        string root = TestData.RepoRoot();
        foreach (string cfg in new[] { "Debug", "Release" })
        {
            string p = Path.Combine(root, "src", "TreeSitter.CodeGen.Cli", "bin", cfg, "net10.0", "tsgen.dll");
            if (File.Exists(p))
                return p;
        }
        // Fallback: search.
        string[] found = Directory.GetFiles(Path.Combine(root, "src", "TreeSitter.CodeGen.Cli"), "tsgen.dll", SearchOption.AllDirectories);
        Assert.NotEmpty(found);
        return found[0];
    }

    private static (int exit, string stdout, string stderr) Run(params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(TsgenDll());
        foreach (string a in args)
            psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)!;
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(60_000);
        return (proc.ExitCode, stdout, stderr);
    }

    [Fact]
    public void No_args_prints_usage_and_returns_1()
    {
        (int exit, string stdout, _) = Run();
        Assert.Equal(1, exit);
        Assert.Contains("tsgen", stdout);
        Assert.Contains("Usage:", stdout);
    }

    [Fact]
    public void Help_flag_returns_0()
    {
        (int exit, string stdout, _) = Run("--help");
        Assert.Equal(0, exit);
        Assert.Contains("Usage:", stdout);
    }

    [Fact]
    public void Missing_required_options_returns_1()
    {
        (int exit, _, string stderr) = Run("--input", "x.json");
        Assert.Equal(1, exit);
        Assert.Contains("missing required option", stderr);
    }

    [Fact]
    public void Nonexistent_input_returns_1()
    {
        (int exit, _, string stderr) = Run(
            "--input", "/no/such/file.json", "--namespace", "X", "--language", "x", "--output", "/tmp/out");
        Assert.Equal(1, exit);
        Assert.Contains("input file not found", stderr);
    }

    [Fact]
    public void Generates_output_file_for_valid_input()
    {
        string? jsonPath = TestData.NodeTypesPath("json");
        if (jsonPath is null)
            return;

        string outDir = Path.Combine(Path.GetTempPath(), "tsgen-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            (int exit, string stdout, string stderr) = Run(
                "--input", jsonPath, "--namespace", "Cli.Test.Json", "--language", "json", "--output", outDir);
            Assert.True(exit == 0, $"exit={exit} stderr={stderr}");
            Assert.Contains("generated", stdout);
            string outFile = Path.Combine(outDir, "Json.Nodes.g.cs");
            Assert.True(File.Exists(outFile));
            Assert.Contains("namespace Cli.Test.Json", File.ReadAllText(outFile));
        }
        finally
        {
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void Equals_form_option_and_short_flags_work()
    {
        string? jsonPath = TestData.NodeTypesPath("json");
        if (jsonPath is null)
            return;

        string outDir = Path.Combine(Path.GetTempPath(), "tsgen-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Exercise the --name=value parsing form and short flags.
            (int exit, _, string stderr) = Run(
                $"--input={jsonPath}", "-n", "Cli.Eq.Json", "-l", "json", "-o", outDir);
            Assert.True(exit == 0, stderr);
            Assert.True(File.Exists(Path.Combine(outDir, "Json.Nodes.g.cs")));
        }
        finally
        {
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void Malformed_input_returns_error_code_2()
    {
        string badFile = Path.Combine(Path.GetTempPath(), "bad-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(badFile, "{ not an array }");
        try
        {
            (int exit, _, string stderr) = Run(
                "--input", badFile, "--namespace", "X", "--language", "x", "--output",
                Path.Combine(Path.GetTempPath(), "out-" + Guid.NewGuid().ToString("N")));
            // A FormatException from the parser surfaces as the catch-all exit code 2.
            Assert.Equal(2, exit);
            Assert.Contains("error:", stderr);
        }
        finally
        {
            File.Delete(badFile);
        }
    }
}
