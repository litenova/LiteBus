#!/bin/bash
# Get the version tag
VERSION="${GITHUB_REF/refs\/tags\/v/}"

# Extract the relevant section from the changelog
awk -v ver="$VERSION" '
  BEGIN { inSection = 0; }
  $0 ~ "^##v." ver { inSection = 1; next; }
  inSection && $0 ~ "^##" { inSection = 0; }
  inSection { print; }
' Changelog.md > release-notes.md

# If no content, provide a default message
if [ ! -s release-notes.md ]; then
  echo "# Release v$VERSION" > release-notes.md
  echo "" >> release-notes.md
  echo "See [Changelog.md](https://github.com/litenova/LiteBus/blob/main/Changelog.md) for details." >> release-notes.md
fi