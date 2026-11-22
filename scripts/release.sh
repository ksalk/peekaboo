#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

fail() {
  echo "release: $*" >&2
  exit 1
}

if [[ ! -f VERSION ]]; then
  fail "VERSION is missing"
fi

version="$(tr -d '[:space:]' < VERSION)"

if [[ ! "$version" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?$ ]]; then
  fail "VERSION must be SemVer (for example 0.1.0 or 0.2.0-rc.1), got '${version}'"
fi

branch="$(git branch --show-current)"
if [[ "$branch" != "main" ]]; then
  fail "releases must be tagged from main, currently on '${branch:-detached HEAD}'"
fi

if [[ -n "$(git status --porcelain)" ]]; then
  fail "working tree is not clean; commit the release changes first"
fi

if [[ ! -f CHANGELOG.md ]] || ! grep -Fq "## [$version]" CHANGELOG.md; then
  fail "CHANGELOG.md has no '## [$version]' section"
fi

tag="v$version"

if git rev-parse --verify --quiet "refs/tags/$tag" >/dev/null; then
  fail "tag $tag already exists"
fi

git tag -a "$tag" -m "Peekaboo $version"

echo "Tagged $tag."
echo
echo "Push the release:"
echo "  git push origin main"
echo "  git push origin $tag"
echo
echo "Publish the image:"
echo "  mise run docker:publish"
