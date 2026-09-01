#!/bin/bash
set -euo pipefail

VERSION="${GITHUB_REF_NAME#v}"

# The release job resolves which Changelog section to read, because a pre-release publishes the notes of the version it
# previews. Fall back to the tag's own heading so the script still works when run on its own.
HEADING="${CHANGELOG_HEADING:-## v${VERSION}}"
IS_PRERELEASE="${IS_PRERELEASE:-false}"

# Extract the relevant section from the changelog
awk -v heading="$HEADING" '
  BEGIN { inSection = 0; }
  $0 == heading { inSection = 1; next; }
  inSection && $0 ~ "^## " { exit; }
  inSection { print; }
' Changelog.md > release-notes.body.md

if [ ! -s release-notes.body.md ]; then
  echo "::error::Release notes for ${HEADING} are empty or missing from Changelog.md."
  rm -f release-notes.body.md
  exit 1
fi

if [ "${IS_PRERELEASE}" = "true" ]; then
  BASE_VERSION="${VERSION%%-*}"
  {
    echo "> **This is a pre-release of ${BASE_VERSION}.** Packages are published to NuGet with the \`${VERSION}\`"
    echo "> version suffix, so \`dotnet add package\` only resolves them when a pre-release version is requested"
    echo "> explicitly or \`--prerelease\` is passed. The notes below describe everything landing in ${BASE_VERSION};"
    echo "> parts of it may still change before the stable release."
    echo
  } > release-notes.md
  cat release-notes.body.md >> release-notes.md
  rm -f release-notes.body.md
else
  mv release-notes.body.md release-notes.md
fi
