#!/usr/bin/env bash
# Fails when test sources contain [Fact(Skip = ...)] attributes.
#
# Waiver policy (see build-and-test.yml):
#   - Allowed only on branches named quarantine until GA sign-off.
#   - SkippableFact and Trait-based quarantine are not gated here.
set -euo pipefail

repo_root="${1:-.}"
cd "${repo_root}"

if git rev-parse --verify HEAD >/dev/null 2>&1; then
  if git branch --show-current 2>/dev/null | grep -qi '^quarantine$'; then
    echo "On quarantine branch; [Fact(Skip = ...)] gate waived until GA sign-off."
    exit 0
  fi
fi

if [[ "${GITHUB_HEAD_REF:-}" =~ ^[Qq]uarantine$ ]] || [[ "${GITHUB_REF:-}" == "refs/heads/quarantine" ]]; then
  echo "Quarantine branch detected via GitHub context; [Fact(Skip = ...)] gate waived until GA sign-off."
  exit 0
fi

matches="$(grep -R --exclude-dir='bin' --exclude-dir='obj' --include='*.cs' -n '\[Fact(Skip' tests 2>/dev/null || true)"

if [[ -n "${matches}" ]]; then
  echo "::error::Found [Fact(Skip = ...)] in test projects. Remove skips or merge via quarantine branch until GA sign-off."
  echo "${matches}"
  exit 1
fi

echo "No [Fact(Skip = ...)] attributes found under tests/."
