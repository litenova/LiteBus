#!/usr/bin/env bash
# Fails when a VSTest TRX report records one or more skipped tests.
set -euo pipefail

trx_file="${1:-}"
if [[ -z "${trx_file}" ]]; then
  echo "Usage: verify-no-skipped-tests.sh <path-to-trx>"
  exit 2
fi

if [[ ! -f "${trx_file}" ]]; then
  echo "::error::TRX file not found: ${trx_file}"
  exit 1
fi

skipped="$(grep -oE 'skipped="[0-9]+"' "${trx_file}" | head -1 | grep -oE '[0-9]+' || true)"
skipped="${skipped:-0}"

if [[ "${skipped}" -gt 0 ]]; then
  echo "::error::Found ${skipped} skipped test(s) in ${trx_file}. Broker transport jobs must not pass with skipped tests when LITEBUS_CI_STRICT_TRANSPORT is enabled."
  exit 1
fi

echo "No skipped tests in ${trx_file}."
