#!/bin/bash
set -euo pipefail

VERSION="${GITHUB_REF_NAME#v}"
HEADING="## v${VERSION}"

# Extract the relevant section from the changelog
awk -v heading="$HEADING" '
  BEGIN { inSection = 0; }
  $0 == heading { inSection = 1; next; }
  inSection && $0 ~ "^## " { exit; }
  inSection { print; }
' Changelog.md > release-notes.md

if [ ! -s release-notes.md ]; then
  echo "::error::Release notes for ${HEADING} are empty or missing from Changelog.md."
  exit 1
fi
