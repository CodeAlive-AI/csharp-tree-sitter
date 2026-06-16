#!/usr/bin/env bash
#
# fetch-test-grammars.sh — build the core tree-sitter runtime and the grammar subset
# the test suite needs into native/<rid>/.
#
# This is a thin convenience wrapper around the general scripts/fetch-grammar.sh: it
# first builds the core libtree-sitter runtime (from the tree-sitter/tree-sitter
# submodule), then delegates each grammar to fetch-grammar.sh, which reads the embedded
# language_definitions.json manifest for the pinned repo/rev/directory/c_symbol.
#
# Usage:
#   scripts/fetch-test-grammars.sh [<cache-dir>]
#
#   <cache-dir>   Where grammar sources are cloned (default: /tmp/ts-grammars).
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
BUILD="$ROOT_DIR/build/build-native.sh"
FETCH="$SCRIPT_DIR/fetch-grammar.sh"

export CACHE_DIR="${1:-/tmp/ts-grammars}"

# The grammars exercised by the test suite. All are non-`generate` (no tree-sitter CLI
# needed) and cover varied scanner shapes: none (json/c/go/html/css/toml), C scanner
# (python/rust/javascript/lua/yaml), and C++ scanner (cpp/bash). typescript/tsx use a
# grammar sub-directory; the rest map name -> tree_sitter_<name> directly.
TEST_LANGS=(
  json python c go rust javascript cpp bash typescript tsx
  ruby html css lua toml yaml
)

# 1) Core runtime (from the submodule) so a fresh checkout produces every artifact.
echo "==> building core libtree-sitter"
bash "$BUILD"

# 2) Each grammar via the general fetcher (reads the manifest for repo/rev/c_symbol/...).
bash "$FETCH" "${TEST_LANGS[@]}"

echo "done. native libraries are in $ROOT_DIR/native/<rid>/"
