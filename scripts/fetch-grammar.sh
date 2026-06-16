#!/usr/bin/env bash
#
# fetch-grammar.sh — fetch and build ANY grammar from the upstream
# tree-sitter-language-pack manifest (language_definitions.json) into the
# repository's native/<rid>/ directory.
#
# Usage:
#   scripts/fetch-grammar.sh <lang> [<lang>...]      Build the named language(s).
#   scripts/fetch-grammar.sh --all                   Build EVERY manifest language
#                                                    that does not need the CLI.
#   scripts/fetch-grammar.sh --list                  Print all manifest language keys.
#
# Environment:
#   MANIFEST    Path to language_definitions.json
#               (default: src/TreeSitter.LanguagePack/language_definitions.json).
#   CACHE_DIR   Where grammar sources are cloned (default: /tmp/ts-grammars).
#
# For each language the script:
#   1. Reads its entry (repo, rev, branch, directory, generate, c_symbol) from the
#      manifest.
#   2. Shallow-clones the repo at the pinned rev into <CACHE_DIR>/<lang> (idempotent:
#      an existing checkout with the grammar src is reused).
#   3. Locates the grammar source dir: <clone>[/<directory>]/src.
#   4. If generate:true AND the `tree-sitter` CLI is on PATH, runs
#      `tree-sitter generate` in the grammar dir; if the CLI is missing the language
#      is SKIPPED with a clear warning (no pre-generated parser.c to compile).
#   5. Invokes build/build-native.sh <c_symbol> <srcDir>, producing
#      native/<rid>/libtree-sitter-<c_symbol>.<ext>.
#
# It is robust and idempotent; a failure on one language does not abort the rest
# (a summary of failures is printed at the end and the script exits non-zero).
#
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
BUILD="$ROOT_DIR/build/build-native.sh"
MANIFEST="${MANIFEST:-$ROOT_DIR/src/TreeSitter.LanguagePack/language_definitions.json}"
CACHE_DIR="${CACHE_DIR:-/tmp/ts-grammars}"

# --- Prerequisite checks --------------------------------------------------------
if [[ ! -f "$MANIFEST" ]]; then
  echo "error: manifest not found at $MANIFEST (set \$MANIFEST)" >&2
  exit 1
fi
if [[ ! -x "$BUILD" ]]; then
  echo "error: build script not found/executable at $BUILD" >&2
  exit 1
fi

# Prefer jq; fall back to python3. Both can read the manifest.
READER=""
if command -v jq >/dev/null 2>&1; then
  READER="jq"
elif command -v python3 >/dev/null 2>&1; then
  READER="python3"
else
  echo "error: need either jq or python3 to read the manifest" >&2
  exit 1
fi

HAVE_TS_CLI=0
command -v tree-sitter >/dev/null 2>&1 && HAVE_TS_CLI=1

# field <lang> <key> — print a manifest field for a language ('' if absent).
field() {
  local lang="$1" key="$2"
  if [[ "$READER" == "jq" ]]; then
    jq -r --arg l "$lang" --arg k "$key" '.[$l][$k] // ""' "$MANIFEST"
  else
    python3 -c "
import json,sys
d=json.load(open('$MANIFEST'))
v=d.get('$lang',{}).get('$key','')
if isinstance(v,bool): v=str(v).lower()
sys.stdout.write('' if v is None else str(v))
"
  fi
}

# has_lang <lang> — exit 0 if the manifest defines the language.
has_lang() {
  local lang="$1"
  if [[ "$READER" == "jq" ]]; then
    [[ "$(jq -r --arg l "$lang" 'has($l)' "$MANIFEST")" == "true" ]]
  else
    python3 -c "import json,sys; sys.exit(0 if '$lang' in json.load(open('$MANIFEST')) else 1)"
  fi
}

all_langs() {
  if [[ "$READER" == "jq" ]]; then
    jq -r 'keys[]' "$MANIFEST"
  else
    python3 -c "import json; print('\n'.join(sorted(json.load(open('$MANIFEST')).keys())))"
  fi
}

# clone_at_rev <repo> <rev> <branch> <dest>
clone_at_rev() {
  local repo="$1" rev="$2" branch="$3" dest="$4"
  if [[ -d "$dest/.git" ]]; then
    echo "  reusing clone $dest"
    return 0
  fi
  echo "  cloning $repo @ ${rev:-<branch:$branch>} -> $dest"
  rm -rf "$dest"
  git init -q "$dest" || return 1
  git -C "$dest" remote add origin "$repo" || return 1
  if [[ -n "$rev" ]]; then
    # Try to fetch just the pinned commit (servers that allow it); fall back to full.
    if ! git -C "$dest" fetch -q --depth 1 origin "$rev" 2>/dev/null; then
      git -C "$dest" fetch -q origin || return 1
    fi
    git -C "$dest" checkout -q "$rev" || return 1
  else
    # No rev pinned: shallow-clone the branch tip.
    local b="${branch:-HEAD}"
    git -C "$dest" fetch -q --depth 1 origin "$b" || git -C "$dest" fetch -q origin || return 1
    git -C "$dest" checkout -q FETCH_HEAD || return 1
  fi
  return 0
}

FAILED=()
SKIPPED=()
BUILT=()

build_one() {
  local lang="$1"

  if ! has_lang "$lang"; then
    echo "==> $lang: NOT in manifest — skipping" >&2
    FAILED+=("$lang (unknown)")
    return
  fi

  local repo rev branch directory generate c_symbol
  repo="$(field "$lang" repo)"
  rev="$(field "$lang" rev)"
  branch="$(field "$lang" branch)"
  directory="$(field "$lang" directory)"
  generate="$(field "$lang" generate)"
  c_symbol="$(field "$lang" c_symbol)"
  [[ -z "$c_symbol" ]] && c_symbol="$lang"

  echo "==> $lang  (c_symbol=$c_symbol, generate=${generate:-false}${directory:+, dir=$directory})"

  if [[ -z "$repo" ]]; then
    echo "  error: no repo for $lang" >&2
    FAILED+=("$lang (no repo)")
    return
  fi

  local dest="$CACHE_DIR/$lang"
  if ! clone_at_rev "$repo" "$rev" "$branch" "$dest"; then
    echo "  error: clone failed for $lang" >&2
    FAILED+=("$lang (clone failed)")
    return
  fi

  # Grammar source dir: <clone>[/<directory>]/src
  local grammar_dir="$dest"
  [[ -n "$directory" ]] && grammar_dir="$dest/$directory"
  local src_dir="$grammar_dir/src"

  # generate:true grammars ship grammar.js but no parser.c — needs the CLI.
  if [[ "$generate" == "true" ]]; then
    if [[ "$HAVE_TS_CLI" -eq 1 ]]; then
      echo "  running 'tree-sitter generate' in $grammar_dir"
      local abi
      abi="$(field "$lang" abi_version)"
      ( cd "$grammar_dir" && tree-sitter generate ${abi:+--abi "$abi"} ) || {
        echo "  error: tree-sitter generate failed for $lang" >&2
        FAILED+=("$lang (generate failed)")
        return
      }
    elif [[ ! -f "$src_dir/parser.c" ]]; then
      echo "  skipped: '$lang' needs the tree-sitter CLI (generate:true) and no parser.c is present" >&2
      SKIPPED+=("$lang (needs tree-sitter CLI)")
      return
    fi
  fi

  if [[ ! -f "$src_dir/parser.c" ]]; then
    echo "  error: no parser.c found at $src_dir (grammar may need 'generate')" >&2
    FAILED+=("$lang (no parser.c)")
    return
  fi

  if bash "$BUILD" "$c_symbol" "$src_dir"; then
    BUILT+=("$lang")
  else
    echo "  error: build failed for $lang" >&2
    FAILED+=("$lang (build failed)")
  fi
}

# --- Argument handling ----------------------------------------------------------
if [[ $# -eq 0 ]]; then
  echo "usage: $0 <lang> [<lang>...] | --all | --list" >&2
  exit 2
fi

case "${1:-}" in
  --list)
    all_langs
    exit 0
    ;;
  --all)
    mkdir -p "$CACHE_DIR"
    while IFS= read -r l; do build_one "$l"; done < <(all_langs)
    ;;
  *)
    mkdir -p "$CACHE_DIR"
    for l in "$@"; do build_one "$l"; done
    ;;
esac

echo
echo "==== summary ===="
echo "built:   ${BUILT[*]:-(none)}"
[[ ${#SKIPPED[@]} -gt 0 ]] && echo "skipped: ${SKIPPED[*]}"
[[ ${#FAILED[@]} -gt 0 ]] && echo "FAILED:  ${FAILED[*]}"
echo "native libraries are in $ROOT_DIR/native/<rid>/"

[[ ${#FAILED[@]} -eq 0 ]]
