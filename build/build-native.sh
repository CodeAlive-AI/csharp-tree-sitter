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
# The portable .NET RID (linux-x64, osx-arm64, osx-x64) is detected automatically
# from `uname -s` / `uname -m`. Output goes to <repoRoot>/native/<rid>/ with the
# platform-correct extension (.so on Linux, .dylib on macOS).
#
# Platform notes:
#   * Linux  -> gcc/clang -shared, ELF soname via -Wl,-soname, POSIX feature macros.
#   * macOS  -> clang -dynamiclib (there is NO -shared semantics for a real dylib),
#               install name @rpath/<lib> (there is no -Wl,-soname on mac), and an
#               ad-hoc codesign of every produced .dylib. On Apple Silicon (arm64)
#               an UNSIGNED dylib is SIGKILLed by the kernel on dlopen, so signing
#               is mandatory, not cosmetic.
#
set -euo pipefail

# --- Locate repository root (this script lives in <root>/build). ----------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
TS_DIR="$ROOT_DIR/tree-sitter/tree-sitter"

# --- Detect OS / arch -> portable RID, library extension, link flags, compilers. -
detect_platform() {
  local uname_s uname_m os arch
  uname_s="$(uname -s)"
  uname_m="$(uname -m)"

  # OS -> os token + library extension. IS_MAC drives the dylib-specific paths.
  case "$uname_s" in
    Linux)
      os="linux"
      LIB_EXT="so"
      IS_MAC=0
      ;;
    Darwin)
      os="osx"
      LIB_EXT="dylib"
      IS_MAC=1
      ;;
    *)
      echo "error: unsupported OS '$uname_s'" >&2
      exit 1
      ;;
  esac

  # Machine -> arch token (portable-RID form). Capture the clang -arch value too.
  case "$uname_m" in
    x86_64|amd64)
      arch="x64"
      ARCH_FLAG="x86_64"
      ;;
    aarch64|arm64)
      arch="arm64"
      ARCH_FLAG="arm64"
      ;;
    *)
      echo "error: unsupported architecture '$uname_m'" >&2
      exit 1
      ;;
  esac

  RID="${os}-${arch}"
  OUT_DIR="$ROOT_DIR/native/$RID"

  # Compiler selection:
  #   * macOS ships clang/clang++ as the system toolchain; gcc is usually a clang
  #     shim. Prefer clang there so -dynamiclib / -install_name behave as documented.
  #   * Linux prefers gcc/g++, then clang/clang++. CC/CXX overrides win on both.
  if [[ "$IS_MAC" -eq 1 ]]; then
    CC="${CC:-$(command -v clang || command -v cc)}"
    CXX="${CXX:-$(command -v clang++ || command -v c++)}"
  else
    CC="${CC:-$(command -v gcc || command -v clang)}"
    CXX="${CXX:-$(command -v g++ || command -v clang++)}"
  fi

  if [[ -z "$CC" ]]; then
    echo "error: no C compiler found (set \$CC)" >&2
    exit 1
  fi
}

# Common portability defines used by the upstream Makefile. On macOS _DARWIN_C_SOURCE
# unlocks the BSD extensions tree-sitter relies on; on Linux the _POSIX/_BSD/_DEFAULT
# trio does the same. Harmless on the other platform.
PORTABILITY_DEFS=(-D_POSIX_C_SOURCE=200112L -D_DEFAULT_SOURCE -D_BSD_SOURCE -D_DARWIN_C_SOURCE)

# Emit the link flags that turn an object into a shared library for the current
# platform, given the bare library file name (e.g. libtree-sitter.dylib). The result
# is printed space-separated for the caller to splat into the compiler invocation.
#   macOS: -dynamiclib + -arch <arch> + -install_name @rpath/<lib>
#   Linux: -shared + -Wl,-soname,<lib>
shared_link_flags() {
  local lib_name="$1"
  if [[ "$IS_MAC" -eq 1 ]]; then
    printf -- '-dynamiclib -arch %s -install_name @rpath/%s' "$ARCH_FLAG" "$lib_name"
  else
    printf -- '-shared -Wl,-soname,%s' "$lib_name"
  fi
}

# Ad-hoc codesign a freshly built dylib on macOS. Unsigned dylibs are killed on load
# on Apple Silicon, so this is required for the library to be usable at all. Guarded
# so the script still works on a mac without codesign on PATH (and is a no-op on Linux).
codesign_if_mac() {
  local file="$1"
  if [[ "$IS_MAC" -eq 1 ]]; then
    if command -v codesign >/dev/null 2>&1; then
      # --force overwrites any linker-applied signature with our ad-hoc one ("-").
      codesign --sign - --force "$file"
      echo "Ad-hoc signed $file"
    else
      echo "warning: codesign not found; '$file' is unsigned and may be killed on load (arm64)." >&2
    fi
  fi
}

build_core() {
  local lib_name="libtree-sitter.${LIB_EXT}"
  local out="$OUT_DIR/$lib_name"

  if [[ ! -f "$TS_DIR/lib/src/lib.c" ]]; then
    echo "error: tree-sitter submodule not found at $TS_DIR (did you init submodules?)" >&2
    exit 1
  fi

  echo "Building core runtime -> $out  (rid=$RID, cc=$CC)"
  mkdir -p "$OUT_DIR"

  # shellcheck disable=SC2046  # intentional word-splitting of the flag string.
  "$CC" -O2 -std=c11 -fPIC -fvisibility=hidden \
    "${PORTABILITY_DEFS[@]}" \
    -I"$TS_DIR/lib/src" \
    -I"$TS_DIR/lib/src/wasm" \
    -I"$TS_DIR/lib/include" \
    $(shared_link_flags "$lib_name") \
    "$TS_DIR/lib/src/lib.c" \
    -o "$out"

  codesign_if_mac "$out"
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

  # parser.c is always C11. An optional external scanner is either C (scanner.c) or
  # C++ (scanner.cc); a C++ scanner forces the whole link through the C++ driver so
  # the C++ runtime is pulled in.
  local sources=("$src_dir/parser.c")
  local use_cpp=0
  if [[ -f "$src_dir/scanner.cc" ]]; then
    sources+=("$src_dir/scanner.cc")
    use_cpp=1
  elif [[ -f "$src_dir/scanner.c" ]]; then
    sources+=("$src_dir/scanner.c")
  fi

  echo "Building grammar '$name' -> $out  (rid=$RID, scanner=$([[ $use_cpp == 1 ]] && echo c++ || echo c/none))"

  if [[ $use_cpp -eq 1 ]]; then
    if [[ -z "$CXX" ]]; then
      echo "error: a C++ scanner was found but no C++ compiler is available (set \$CXX)" >&2
      exit 1
    fi
    # C++ scanner: compile with the C++ compiler and link the C++ runtime (-lstdc++
    # on Linux; clang++ links libc++ automatically on macOS but -lstdc++ is harmless
    # there too, so we let the driver decide and only pass it on Linux).
    local cpp_runtime=()
    [[ "$IS_MAC" -eq 0 ]] && cpp_runtime=(-lstdc++)
    # shellcheck disable=SC2046
    "$CXX" -O2 -std=c++14 -fPIC -fvisibility=hidden \
      -I"$src_dir" \
      $(shared_link_flags "$lib_name") \
      "${sources[@]}" \
      "${cpp_runtime[@]}" \
      -o "$out"
  else
    # shellcheck disable=SC2046
    "$CC" -O2 -std=c11 -fPIC -fvisibility=hidden \
      -I"$src_dir" \
      $(shared_link_flags "$lib_name") \
      "${sources[@]}" \
      -o "$out"
  fi

  codesign_if_mac "$out"
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
