#!/bin/bash
set -euo pipefail

VERSION="${GITHUB_REF_NAME#v}"

# The release job resolves which Changelog section to read and whether it describes the whole version or only what
# changed since the previous pre-release. Fall back to the tag's own heading so the script still works when run on
# its own, in which case a section for the tag is a delta and anything else is cumulative.
HEADING="${CHANGELOG_HEADING:-## v${VERSION}}"
IS_PRERELEASE="${IS_PRERELEASE:-false}"
BASE_VERSION="${VERSION%%-*}"

if [ -n "${CHANGELOG_SCOPE:-}" ]; then
  SCOPE="${CHANGELOG_SCOPE}"
elif [ "${HEADING}" = "## v${VERSION}" ] && [ "${IS_PRERELEASE}" = "true" ]; then
  SCOPE=delta
else
  SCOPE=cumulative
fi

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

if [ "${IS_PRERELEASE}" != "true" ]; then
  mv release-notes.body.md release-notes.md
  exit 0
fi

# The cumulative notes are one link away from a delta. Read at the tag rather than on the default branch: a
# pre-release is cut from its version's branch, so the section it previews has usually not reached main yet.
CHANGELOG_ANCHOR="$(printf '%s' "${BASE_VERSION}" | tr -d '.')"
CHANGELOG_URL="https://github.com/${GITHUB_REPOSITORY:-litenova/LiteBus}/blob/${GITHUB_REF_NAME}/Changelog.md#v${CHANGELOG_ANCHOR}"

{
  echo "> **This is a pre-release of ${BASE_VERSION}.** Packages are published to NuGet with the \`${VERSION}\`"
  echo "> version suffix, so \`dotnet add package\` only resolves them when a pre-release version is requested"
  echo "> explicitly or \`--prerelease\` is passed."

  if [ "${SCOPE}" = "delta" ]; then
    PREVIOUS="$(printf '%s' "${VERSION}" | awk -F. '{ if ($NF ~ /^[0-9]+$/ && $NF > 1) { $NF = $NF - 1; print } }' OFS=.)"

    echo ">"

    if [ -n "${PREVIOUS}" ]; then
      echo "> The notes below cover what changed since \`${PREVIOUS}\`. For everything landing in"
    else
      echo "> The notes below cover what changed since the previous pre-release. For everything landing in"
    fi

    echo "> ${BASE_VERSION}, see the [${BASE_VERSION} changelog](${CHANGELOG_URL})."
  else
    echo "> The notes below describe everything landing in ${BASE_VERSION}; parts of it may still change before the"
    echo "> stable release."
  fi

  echo
} > release-notes.md

cat release-notes.body.md >> release-notes.md
rm -f release-notes.body.md
