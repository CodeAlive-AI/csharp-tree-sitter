#!/usr/bin/env bash
#
# fetch-test-grammars.sh — clone the tree-sitter grammar repos used by the test
# suite at pinned revisions and build each grammar into native/<rid>/ via
# build/build-native.sh.
#
# Usage:
#   scripts/fetch-test-grammars.sh [<work-dir>]
#
#   <work-dir>   Directory to clone grammar sources into (default: /tmp/grammars).
#
# The script is idempotent: a grammar whose source directory already exists at the
# pinned rev is reused rather than re-cloned. It also builds the core libtree-sitter
# runtime first (from the tree-sitter/tree-sitter submodule) so a fresh checkout that
# ran `git submodule update --init --recursive` can produce every native artifact the
# tests need in one step.
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
WORK_DIR="${1:-/tmp/grammars}"
BUILD="$ROOT_DIR/build/build-native.sh"

# name|repo|rev — pinned revisions that produce the node-types.json the tests assert
# against. json is intentionally an older revision (ABI 14) to match the checked-in
# generated bindings; the rest are ABI 15 (exercise Name/Supertypes/Subtypes).
GRAMMARS=(
  "json|https://github.com/tree-sitter/tree-sitter-json.git|001c28d7a29832b06b0e831ec77845553c89b56d"
  "python|https://github.com/tree-sitter/tree-sitter-python.git|26855eabccb19c6abf499fbc5b8dc7cc9ab8bc64"
  "c|https://github.com/tree-sitter/tree-sitter-c.git|b780e47fc780ddc8da13afa35a3f4ed5c157823d"
  "go|https://github.com/tree-sitter/tree-sitter-go.git|2346a3ab1bb3857b48b29d779a1ef9799a248cd7"
  "rust|https://github.com/tree-sitter/tree-sitter-rust.git|77a3747266f4d621d0757825e6b11edcbf991ca5"
  "javascript|https://github.com/tree-sitter/tree-sitter-javascript.git|58404d8cf191d69f2674a8fd507bd5776f46cb11"
)

mkdir -p "$WORK_DIR"

clone_at_rev() {
  local repo="$1" rev="$2" dest="$3"
  if [[ -f "$dest/src/parser.c" ]]; then
    echo "reusing $dest"
    return
  fi
  echo "cloning $repo @ $rev -> $dest"
  rm -rf "$dest"
  git init -q "$dest"
  git -C "$dest" remote add origin "$repo"
  # Fetch just the pinned commit when the server allows it; fall back to a full fetch.
  if ! git -C "$dest" fetch -q --depth 1 origin "$rev"; then
    git -C "$dest" fetch -q origin
  fi
  git -C "$dest" checkout -q "$rev"
}

# 1) Core runtime (from the submodule).
echo "==> building core libtree-sitter"
bash "$BUILD"

# 2) Each grammar.
for entry in "${GRAMMARS[@]}"; do
  IFS='|' read -r name repo rev <<<"$entry"
  dest="$WORK_DIR/$name"
  clone_at_rev "$repo" "$rev" "$dest"
  echo "==> building grammar '$name'"
  bash "$BUILD" "$name" "$dest/src"
done

echo "done. native libraries are in $ROOT_DIR/native/<rid>/"
