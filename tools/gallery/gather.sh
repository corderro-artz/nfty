#!/usr/bin/env bash
# Collects, as DATA, everything check.py reads about one pull request. Runs in check.yml with the read-only
# GITHUB_TOKEN. Nothing from the pull request is checked out or executed: its file list comes from the API and
# the entry file is fetched as bytes at the PR's head commit.
#
# Environment (all set by check.yml from the event, never interpolated into this script):
#   GH_TOKEN  REPO (gallery owner/name)  PR (number)  HEAD_REPO (owner/name of the PR's branch)  HEAD_SHA  OUT (work dir)
set -euo pipefail

: "${GH_TOKEN:?}" "${REPO:?}" "${PR:?}" "${HEAD_REPO:?}" "${HEAD_SHA:?}" "${OUT:?}"
[[ "$PR" =~ ^[0-9]+$ ]] || { echo "bad PR number" >&2; exit 2; }
[[ "$HEAD_SHA" =~ ^[0-9a-f]{40}$ ]] || { echo "bad head sha" >&2; exit 2; }
[[ "$HEAD_REPO" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] || { echo "bad head repo" >&2; exit 2; }
ENTRY_RE='^entries/[0-9]+/[a-z0-9-]+\.json$'
mkdir -p "$OUT/pr" "$OUT/fetch" "$OUT/out"

raw() { # repo path ref -> file bytes, capped (an entry is a few hundred bytes)
  gh api -H "Accept: application/vnd.github.raw" "repos/$1/contents/$2?ref=$3" | head -c 65536
}

gh api "repos/$REPO/pulls/$PR" > "$OUT/pr.json"
gh api --paginate "repos/$REPO/pulls/$PR/files" | jq -s 'add // []' > "$OUT/pr-files.json"

author=$(jq -r '.user.login' "$OUT/pr.json")
[[ "$author" =~ ^[A-Za-z0-9-]+$ ]] || { echo "odd author login" >&2; exit 2; }
echo "$author" > "$OUT/author-login"
gh api "users/$author" > "$OUT/author.json"

# The entry the PR adds or changes (at most one is read; check.py refuses a PR with more).
: > "$OUT/entry-path"
while IFS= read -r path; do
  if [[ "$path" =~ $ENTRY_RE ]]; then
    mkdir -p "$OUT/pr/$(dirname "$path")"
    raw "$HEAD_REPO" "$path" "$HEAD_SHA" > "$OUT/pr/$path"
    if [ ! -s "$OUT/entry-path" ]; then echo "$OUT/pr/$path" > "$OUT/entry-path"; fi
  fi
done < <(jq -r '.[] | select(.status != "removed") | .filename' "$OUT/pr-files.json")

# The author's nfty-public, as the repo check reads it.
slug_repo="$author/nfty-public"
if gh api "repos/$slug_repo" > "$OUT/repo-api.json" 2>/dev/null; then
  branch=$(jq -r '.default_branch' "$OUT/repo-api.json")
  gh api "repos/$slug_repo/git/trees/$branch?recursive=1" > "$OUT/tree.json"
  gh api --paginate "repos/$slug_repo/branches" | jq -s '[(add // [])[].name]' > "$OUT/branches.json"
  gh api --paginate "repos/$slug_repo/tags" | jq -s '[(add // [])[].name]' > "$OUT/tags-of-repo.json"
  gh api --paginate "repos/$slug_repo/releases" | jq -s 'add // []' > "$OUT/releases.json"
  if raw "$slug_repo" nfty-public.json "$branch" 2>/dev/null | jq -c . > "$OUT/manifest.json" 2>/dev/null \
     && [ -s "$OUT/manifest.json" ]; then :; else echo null > "$OUT/manifest.json"; fi
  jq --slurpfile b "$OUT/branches.json" --slurpfile t "$OUT/tags-of-repo.json" --slurpfile m "$OUT/manifest.json" \
     '. + {branches: $b[0], tags: $t[0], manifest: $m[0]}' "$OUT/repo-api.json" > "$OUT/repo.json"
else
  echo null > "$OUT/repo.json"; echo '{"tree":[]}' > "$OUT/tree.json"; echo '[]' > "$OUT/releases.json"
fi

# Entries of the gallery's OTHER open pull requests: untrusted, used only for duplicates and the daily rate.
: > "$OUT/open-entries.ndjson"
gh api --paginate "repos/$REPO/pulls?state=open&per_page=100" | jq -s 'add // []' > "$OUT/open-prs.json"
count=0
while IFS=$'\t' read -r number head_repo head_sha; do
  [ "$number" = "$PR" ] && continue
  count=$((count + 1)); [ "$count" -gt 50 ] && break
  [[ "$head_sha" =~ ^[0-9a-f]{40}$ && "$head_repo" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] || continue
  while IFS= read -r path; do
    [[ "$path" =~ $ENTRY_RE ]] || continue
    raw "$head_repo" "$path" "$head_sha" 2>/dev/null | jq -c . >> "$OUT/open-entries.ndjson" 2>/dev/null || true
  done < <(gh api "repos/$REPO/pulls/$number/files" --jq '.[] | select(.status != "removed") | .filename' 2>/dev/null || true)
done < <(jq -r '.[] | [.number, .head.repo.full_name, .head.sha] | @tsv' "$OUT/open-prs.json")
jq -s '.' "$OUT/open-entries.ndjson" > "$OUT/open-entries.json"
