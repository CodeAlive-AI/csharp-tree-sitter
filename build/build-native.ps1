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

function Build-WithClang {
    param([string]$OutFile, [string[]]$Sources, [string[]]$Includes, [bool]$UseCpp)

    $compiler = if ($UseCpp) { 'clang++' } else { 'clang' }
    $std = if ($UseCpp) { '-std=c++14' } else { '-std=c11' }
    $incArgs = $Includes | ForEach-Object { "-I$_" }

    Write-Host "Compiling with $compiler -> $OutFile"
    & $compiler -O2 $std -shared -o $OutFile @incArgs @Sources
    if ($LASTEXITCODE -ne 0) { throw "$compiler failed with exit code $LASTEXITCODE" }
}

function Build-WithMsvc {
    param([string]$OutFile, [string[]]$Sources, [string[]]$Includes, [bool]$UseCpp)

    if (-not (Test-Command 'cl')) {
        throw "Neither clang nor cl.exe was found on PATH. Run from a Developer PowerShell for VS, or install LLVM."
    }

    $incArgs = $Includes | ForEach-Object { "/I$_" }
    $stdArg = if ($UseCpp) { '/std:c++14' } else { '/std:c11' }

    Write-Host "Compiling with cl.exe -> $OutFile"
    # /LD produces a DLL; /Fe sets the output name.
    & cl /nologo /O2 $stdArg /LD @incArgs @Sources "/Fe:$OutFile"
    if ($LASTEXITCODE -ne 0) { throw "cl.exe failed with exit code $LASTEXITCODE" }
}

function Invoke-Build {
    param([string]$OutFile, [string[]]$Sources, [string[]]$Includes, [bool]$UseCpp)

    if (Test-Command 'clang') {
        Build-WithClang -OutFile $OutFile -Sources $Sources -Includes $Includes -UseCpp $UseCpp
    }
    else {
        Build-WithMsvc -OutFile $OutFile -Sources $Sources -Includes $Includes -UseCpp $UseCpp
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
    Invoke-Build -OutFile $out -Sources @($libC) -Includes $includes -UseCpp $false
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
