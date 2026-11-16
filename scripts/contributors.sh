#!/usr/bin/env bash
# The people whose work is in the history, by GitHub account: the authors of the commits on the default branch (as GitHub maps them to
# accounts) and everyone a commit names in a Co-authored-by line with a GitHub address. Bots and AI co-authors are left out; no e-mail
# address or git name is ever written.
#
#   scripts/contributors.sh                  rewrites CONTRIBUTORS.md
#   scripts/contributors.sh --since v1.2.3   prints the release notes' thanks line for what came after that tag
#
# Needs the gh CLI, signed in, and the repository's GitHub remote.
set -euo pipefail

since=""
if [ "${1:-}" = "--since" ]; then
  since="${2:?--since needs a tag, e.g. --since v0.20.0}"
  git rev-parse --verify --quiet "$since^{commit}" >/dev/null || { echo "No such tag or commit: $since" >&2; exit 1; }
elif [ $# -gt 0 ]; then
  echo "Usage: scripts/contributors.sh [--since <tag>]" >&2
  exit 1
fi

repo="$(gh repo view --json nameWithOwner --jq .nameWithOwner)"
owner="${repo%%/*}"

# Accounts of the co-authors named in the commits of a range (all of them without one): a GitHub noreply address is
# "<login>@users.noreply.github.com" or "<id>+<login>@users.noreply.github.com". Any other address cannot be mapped to an account
# without asking for it, so it is counted and said, never printed.
coauthors() {
  local unmapped=0 line
  while IFS= read -r line; do
    [ -n "$line" ] || continue
    case "$line" in
      *"@users.noreply.github.com>"*)
        line="${line##*<}"; line="${line%%@*}"; echo "${line#*+}" ;;
      *) unmapped=$((unmapped + 1)) ;;
    esac
  done < <(git log --format='%(trailers:key=Co-authored-by,valueonly)' "$@" | grep -viE 'claude|anthropic|copilot|\[bot\]' || true)
  [ "$unmapped" -eq 0 ] || echo "$unmapped co-author line(s) carry no GitHub address and were left out; add the person by a noreply address or a .mailmap entry." >&2
}

# One login per line in, the same out: no bots, no doubles, in alphabetical order whatever the case.
people() { grep -vE '\[bot\]$|^$' | sort -fu; }

if [ -n "$since" ]; then
  logins="$( { gh api "repos/$repo/compare/$since...HEAD" --paginate --jq '.commits[].author | select(. != null and .type == "User") | .login'; coauthors "$since..HEAD"; } | people | grep -vixF "$owner" || true)"
  if [ -z "$logins" ]; then
    echo "Nobody but @$owner contributed since $since: no thanks line." >&2
    exit 0
  fi
  # shellcheck disable=SC2001
  echo "Thanks to $(echo "$logins" | sed 's/^/@/' | paste -sd, - | sed 's/,/, /g') for their contributions."
  exit 0
fi

logins="$( { gh api "repos/$repo/contributors?per_page=100" --paginate --jq '.[] | select(.type == "User") | .login'; coauthors; } | people)"
[ -n "$logins" ] || { echo "GitHub named no contributor: nothing written." >&2; exit 1; }

{
  echo "# Contributors"
  echo
  echo "Everyone whose work is in Tankstat, by GitHub account, in alphabetical order. Thank you."
  echo
  while IFS= read -r login; do echo "- [@$login](https://github.com/$login)"; done <<< "$logins"
  echo
  echo "This list is made from the history by \`mise run contributors\` for every release: the authors of the commits on \`main\` and everyone a commit names as a co-author. Automated accounts and AI co-authors are not listed. If your work is in a release and your name is missing, [open an issue](https://github.com/$repo/issues/new)."
} > CONTRIBUTORS.md
echo "CONTRIBUTORS.md: $(echo "$logins" | wc -l | tr -d ' ') people."
