#!/usr/bin/env bash
# Stages docs/wiki into .site-docs so MkDocs can build it. The wiki sources are never modified:
# only the staged copy gets Home.md renamed to index.md and the repo-relative links rewritten.
set -euo pipefail

cd "$(dirname "$0")/../.."

out=".site-docs"
rm -rf "$out"
mkdir -p "$out/assets"

cp docs/wiki/*.md "$out/"
cp docs/assets/* "$out/assets/"
cp CHANGELOG.md "$out/CHANGELOG.md"
{
  printf -- '---\ntitle: Background job processing for .NET\n'
  printf 'description: Predictable, observable background jobs for .NET 8 with retries, deadlines, dead-letter handling, a dashboard and five storage providers.\n---\n\n'
  cat "$out/Home.md"
} > "$out/index.md"
rm "$out/Home.md"

sed -i 's|\.\./assets/|assets/|g; s|\.\./\.\./CHANGELOG\.md|CHANGELOG.md|g' "$out"/*.md
