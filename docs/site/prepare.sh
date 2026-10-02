#!/usr/bin/env bash
# Stages docs/wiki into .site-docs so MkDocs can build it.
set -euo pipefail

cd "$(dirname "$0")/../.."

out=".site-docs"
rm -rf "$out"
mkdir -p "$out/assets"

# Recursively copy all wiki content
cp -r docs/wiki/* "$out/"
cp docs/assets/* "$out/assets/"
mkdir -p "$out/reference"
cp CHANGELOG.md "$out/reference/changelog.md"

# Normalize asset and changelog links
find "$out" -name '*.md' -exec sed -i 's|\.\./assets/|assets/|g; s|\.\./\.\./CHANGELOG\.md|reference/changelog.md|g' {} +
