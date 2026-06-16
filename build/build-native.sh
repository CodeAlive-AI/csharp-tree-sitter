#!/usr/bin/env bash
#
# build-native.sh — build the tree-sitter core runtime and/or a grammar into
# the repository's native/<rid>/ directory.
#
# Usage:
#   build-native.sh                       Build libtree-sitter (core runtime).
#   build-native.sh <name> <srcDir>       Build a grammar from <srcDir> (which
#                                         must contain parser.c, optionally
#                                         scanner.c / scanner.cc) into
#                                         libtree-sitter-<name>.<ext>.
#
# The RID (linux-x64, linux-arm64, osx-x64, osx-arm64) is detected automatically.
# Output goes to <repoRoot>/native/<rid>/.
#
set -euo pipefail

# --- Locate repository root (this script lives in <root>/build). ----------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
TS_DIR="$ROOT_DIR/tree-sitter/tree-sitter"

# --- Detect OS / arch -> RID, shared-library extension, and compilers. ----------
detect_platform() {
  local uname_s uname_m os arch
  uname_s="$(uname -s)"
  uname_m="$(uname -m)"

  case "$uname_s" in
    Linux)  os="linux"; LIB_EXT="so";    SHARED_FLAGS="-shared" ;;
    Darwin) os="osx";   LIB_EXT="dylib"; SHARED_FLAGS="-dynamiclib" ;;
    *) echo "error: unsupported OS '$uname_s'" >&2; exit 1 ;;
  esac

  case "$uname_m" in
    x86_64|amd64)        arch="x64" ;;
    aarch64|arm64)       arch="arm64" ;;
    *) echo "error: unsupported architecture '$uname_m'" >&2; exit 1 ;;
  esac

  RID="${os}-${arch}"
  OUT_DIR="$ROOT_DIR/native/$RID"

  # Allow CC/CXX overrides; otherwise prefer gcc/g++, then clang/clang++.
  CC="${CC:-$(command -v gcc || command -v clang)}"
  CXX="${CXX:-$(command -v g++ || command -v clang++)}"
}

# Common portability defines used by the upstream Makefile.
PORTABILITY_DEFS=(-D_POSIX_C_SOURCE=200112L -D_DEFAULT_SOURCE -D_BSD_SOURCE -D_DARWIN_C_SOURCE)

build_core() {
  local lib_name="libtree-sitter.${LIB_EXT}"
  local out="$OUT_DIR/$lib_name"

  if [[ ! -f "$TS_DIR/lib/src/lib.c" ]]; then
    echo "error: tree-sitter submodule not found at $TS_DIR (did you init submodules?)" >&2
    exit 1
  fi

  echo "Building core runtime -> $out  (rid=$RID, cc=$CC)"
  mkdir -p "$OUT_DIR"

  "$CC" -O2 -std=c11 -fPIC -fvisibility=hidden \
    "${PORTABILITY_DEFS[@]}" \
    -I"$TS_DIR/lib/src" \
    -I"$TS_DIR/lib/src/wasm" \
    -I"$TS_DIR/lib/include" \
    $SHARED_FLAGS \
    "$TS_DIR/lib/src/lib.c" \
    -o "$out"

  echo "Built $out"
}

build_grammar() {
  local name="$1"
  local src_dir="$2"
  local lib_name="libtree-sitter-${name}.${LIB_EXT}"
  local out="$OUT_DIR/$lib_name"

  if [[ ! -f "$src_dir/parser.c" ]]; then
    echo "error: $src_dir/parser.c not found" >&2
    exit 1
  fi

  mkdir -p "$OUT_DIR"

  local sources=("$src_dir/parser.c")
  local use_cpp=0

  # Detect an external scanner: prefer C++ (scanner.cc) then C (scanner.c).
  if [[ -f "$src_dir/scanner.cc" ]]; then
    sources+=("$src_dir/scanner.cc")
    use_cpp=1
  elif [[ -f "$src_dir/scanner.c" ]]; then
    sources+=("$src_dir/scanner.c")
  fi

  echo "Building grammar '$name' -> $out  (rid=$RID, scanner=$([[ $use_cpp == 1 ]] && echo c++ || echo c/none))"

  if [[ $use_cpp == 1 ]]; then
    # C++ scanner: compile with the C++ compiler and link the C++ runtime.
    "$CXX" -O2 -std=c++14 -fPIC -fvisibility=hidden \
      -I"$src_dir" \
      $SHARED_FLAGS \
      "${sources[@]}" \
      -lstdc++ \
      -o "$out"
  else
    "$CC" -O2 -std=c11 -fPIC -fvisibility=hidden \
      -I"$src_dir" \
      $SHARED_FLAGS \
      "${sources[@]}" \
      -o "$out"
  fi

  echo "Built $out"
}

main() {
  detect_platform

  if [[ $# -eq 0 ]]; then
    build_core
  elif [[ $# -eq 2 ]]; then
    build_grammar "$1" "$2"
  else
    echo "usage: $0 [<name> <srcDir>]" >&2
    echo "  (no args)            build libtree-sitter core runtime" >&2
    echo "  <name> <srcDir>      build a grammar's parser.c (+scanner) into native/<rid>" >&2
    exit 2
  fi
}

main "$@"
