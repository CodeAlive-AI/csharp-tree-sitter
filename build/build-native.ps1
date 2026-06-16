#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds the tree-sitter core runtime and/or a grammar into native\win-x64\.

.DESCRIPTION
    Windows equivalent of build-native.sh. Produces tree-sitter.dll (core) and
    tree-sitter-<name>.dll (grammars) into <repoRoot>\native\<rid>\.

    Prefers clang (clang / clang++) when available, otherwise falls back to the
    MSVC toolchain (cl.exe). Run from a "Developer PowerShell for VS" if you rely
    on cl.exe so that the compiler and its environment are on PATH.

.PARAMETER Name
    Optional grammar name. When supplied together with -SrcDir, builds that
    grammar instead of the core runtime.

.PARAMETER SrcDir
    The grammar's source directory (must contain parser.c, optionally
    scanner.c / scanner.cc).

.EXAMPLE
    .\build-native.ps1
    Builds tree-sitter.dll (core runtime).

.EXAMPLE
    .\build-native.ps1 -Name json -SrcDir ..\grammars\tree-sitter-json\src
    Builds tree-sitter-json.dll.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Name,

    [Parameter(Position = 1)]
    [string]$SrcDir
)

$ErrorActionPreference = 'Stop'

# --- Locate repository root (this script lives in <root>\build). ----------------
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = (Resolve-Path (Join-Path $ScriptDir '..')).Path
$TsDir = Join-Path $RootDir 'tree-sitter\tree-sitter'

# --- Detect architecture -> RID. ------------------------------------------------
$arch = switch ($env:PROCESSOR_ARCHITECTURE) {
    'AMD64' { 'x64' }
    'ARM64' { 'arm64' }
    'x86'   { 'x86' }
    default { 'x64' }
}
$Rid = "win-$arch"
$OutDir = Join-Path $RootDir "native\$Rid"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$PortabilityDefs = @()  # MSVC/clang-cl define POSIX feature macros themselves.

function Test-Command([string]$cmd) {
    return [bool](Get-Command $cmd -ErrorAction SilentlyContinue)
}

# Generates a module-definition (.def) file listing the FULL public tree-sitter API
# by scanning the vendored header (lib/include/tree_sitter/api.h) for every `ts_*(`
# function declaration, and returns its path. The CORE runtime needs this on Windows:
# unlike grammar parser.c (which #defines TS_PUBLIC to __declspec(dllexport) on _WIN32),
# the core's public API is gated only by `#pragma GCC visibility push(default)` — a
# GCC/clang construct. clang on a *-windows target honours it and emits dllexport, but
# MSVC (cl.exe) ignores it and would export NOTHING (every core P/Invoke would then fail
# at runtime). The SHIPPED tree-sitter/tree-sitter.def is stale (missing the ABI-15
# additions: lookahead iterators, language metadata, parse-with-options, wasm, ...), so
# we synthesize a complete, current .def from the header — exporting exactly the public
# functions for whichever ABI the submodule is pinned at. We pass it to BOTH compilers so
# the export surface is identical and pragma-independent.
#
# NOTE: the Windows build path is not exercised by CI (the matrix is linux-x64/osx-arm64),
# so this is correct-by-construction: the generated .def is a 1:1 image of api.h's public
# declarations, which match the binding's P/Invoke surface exactly.
function Get-CoreExportsDef {
    $apiHeader = Join-Path $TsDir 'lib\include\tree_sitter\api.h'
    if (-not (Test-Path $apiHeader)) {
        throw "tree-sitter api.h not found at $apiHeader (did you init submodules?)"
    }

    # Match a ts_* identifier immediately followed by '(' (allowing whitespace). This
    # captures every public-API prototype; the header contains no other `ts_*(` tokens.
    $text = Get-Content -Raw -Path $apiHeader
    $symbols = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($m in [regex]::Matches($text, '\bts_[A-Za-z0-9_]+(?=\s*\()')) {
        [void]$symbols.Add($m.Value)
    }
    if ($symbols.Count -eq 0) { throw "no ts_* exports found in $apiHeader" }

    $defPath = Join-Path $OutDir 'tree-sitter.def'
    $lines = @('LIBRARY tree-sitter', 'EXPORTS') + ($symbols | ForEach-Object { "    $_" })
    Set-Content -Path $defPath -Value $lines -Encoding ascii
    Write-Host "Wrote $($symbols.Count) core exports -> $defPath"
    return $defPath
}

function Build-WithClang {
    param([string]$OutFile, [string[]]$Sources, [string[]]$Includes, [bool]$UseCpp, [string]$DefFile)

    $compiler = if ($UseCpp) { 'clang++' } else { 'clang' }
    $std = if ($UseCpp) { '-std=c++14' } else { '-std=c11' }
    $incArgs = $Includes | ForEach-Object { "-I$_" }
    # clang on Windows drives the MSVC/lld linker, which consumes a .def via /def:.
    # Belt-and-braces alongside api.h's visibility pragma (clang already emits dllexport
    # for the public funcs); the explicit def pins the full ABI surface deterministically.
    $defArgs = if ($DefFile) { @("-Wl,/def:$DefFile") } else { @() }

    Write-Host "Compiling with $compiler -> $OutFile"
    & $compiler -O2 $std -shared -o $OutFile @incArgs @Sources @defArgs
    if ($LASTEXITCODE -ne 0) { throw "$compiler failed with exit code $LASTEXITCODE" }
}

function Build-WithMsvc {
    param([string]$OutFile, [string[]]$Sources, [string[]]$Includes, [bool]$UseCpp, [string]$DefFile)

    if (-not (Test-Command 'cl')) {
        throw "Neither clang nor cl.exe was found on PATH. Run from a Developer PowerShell for VS, or install LLVM."
    }

    $incArgs = $Includes | ForEach-Object { "/I$_" }
    $stdArg = if ($UseCpp) { '/std:c++14' } else { '/std:c11' }
    # cl ignores api.h's GCC visibility pragma, so the core MUST be given an explicit
    # exports list or the DLL exports nothing. /DEF goes through to the linker.
    $defArgs = if ($DefFile) { @('/link', "/DEF:$DefFile") } else { @() }

    Write-Host "Compiling with cl.exe -> $OutFile"
    # /LD produces a DLL; /Fe sets the output name. Linker args (if any) must come last.
    & cl /nologo /O2 $stdArg /LD @incArgs @Sources "/Fe:$OutFile" @defArgs
    if ($LASTEXITCODE -ne 0) { throw "cl.exe failed with exit code $LASTEXITCODE" }
}

function Invoke-Build {
    param([string]$OutFile, [string[]]$Sources, [string[]]$Includes, [bool]$UseCpp, [string]$DefFile)

    if (Test-Command 'clang') {
        Build-WithClang -OutFile $OutFile -Sources $Sources -Includes $Includes -UseCpp $UseCpp -DefFile $DefFile
    }
    else {
        Build-WithMsvc -OutFile $OutFile -Sources $Sources -Includes $Includes -UseCpp $UseCpp -DefFile $DefFile
    }
}

function Build-Core {
    $out = Join-Path $OutDir 'tree-sitter.dll'
    $libC = Join-Path $TsDir 'lib\src\lib.c'
    if (-not (Test-Path $libC)) {
        throw "tree-sitter submodule not found at $TsDir (did you init submodules?)"
    }

    Write-Host "Building core runtime -> $out  (rid=$Rid)"
    $includes = @(
        (Join-Path $TsDir 'lib\src'),
        (Join-Path $TsDir 'lib\src\wasm'),
        (Join-Path $TsDir 'lib\include')
    )
    # The core's public API is exported via an explicit .def synthesized from api.h
    # (see Get-CoreExportsDef) so it exports the full ABI surface under BOTH clang and
    # cl.exe. Grammars do NOT need this (their parser.c self-exports via __declspec).
    $defFile = Get-CoreExportsDef
    Invoke-Build -OutFile $out -Sources @($libC) -Includes $includes -UseCpp $false -DefFile $defFile
    Write-Host "Built $out"
}

function Build-Grammar {
    param([string]$GrammarName, [string]$GrammarSrc)

    $parser = Join-Path $GrammarSrc 'parser.c'
    if (-not (Test-Path $parser)) { throw "$parser not found" }

    $out = Join-Path $OutDir "tree-sitter-$GrammarName.dll"
    $sources = @($parser)
    $useCpp = $false

    $scannerCc = Join-Path $GrammarSrc 'scanner.cc'
    $scannerC = Join-Path $GrammarSrc 'scanner.c'
    if (Test-Path $scannerCc) {
        $sources += $scannerCc
        $useCpp = $true
    }
    elseif (Test-Path $scannerC) {
        $sources += $scannerC
    }

    $scannerKind = if ($useCpp) { 'c++' } else { 'c/none' }
    Write-Host "Building grammar '$GrammarName' -> $out  (rid=$Rid, scanner=$scannerKind)"
    Invoke-Build -OutFile $out -Sources $sources -Includes @($GrammarSrc) -UseCpp $useCpp
    Write-Host "Built $out"
}

if ([string]::IsNullOrEmpty($Name)) {
    Build-Core
}
elseif ([string]::IsNullOrEmpty($SrcDir)) {
    throw "When a grammar -Name is supplied, -SrcDir is required."
}
else {
    Build-Grammar -GrammarName $Name -GrammarSrc $SrcDir
}
