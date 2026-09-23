#!/bin/bash
# Downloads (once) and caches the official Godot 4.7.2-stable Linux binary
# so the headless tests under scripts/tests/ can run immediately, then
# imports the project so resources are ready. Run a test with:
#   godot --headless --path . --script res://scripts/tests/test_time.gd < /dev/null
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

GODOT_VERSION="4.7.2-stable"
GODOT_NAME="Godot_v${GODOT_VERSION}_linux.x86_64"
# This repo's assets-v1 release mirrors the official zip byte for byte;
# upstream is the fallback.
GODOT_URLS=(
  "https://github.com/supa-bit/Facsimilia/releases/download/assets-v1/${GODOT_NAME}.zip"
  "https://github.com/godotengine/godot/releases/download/${GODOT_VERSION}/${GODOT_NAME}.zip"
)
CACHE_DIR="${HOME}/.cache/godot/${GODOT_VERSION}"
GODOT_BIN="${CACHE_DIR}/${GODOT_NAME}"

if [ ! -x "${GODOT_BIN}" ]; then
  mkdir -p "${CACHE_DIR}"
  tmp_zip="$(mktemp "${CACHE_DIR}/godot.XXXXXX.zip")"
  got=""
  for delay in 2 4 8 16 0; do
    for url in "${GODOT_URLS[@]}"; do
      if curl -fsSL --retry 2 -o "${tmp_zip}" "${url}"; then got=1; break 2; fi
    done
    [ "${delay}" -eq 0 ] && break
    sleep "${delay}"
  done
  [ -n "${got}" ] || { rm -f "${tmp_zip}"; echo "Godot download failed" >&2; exit 1; }
  unzip -o -q "${tmp_zip}" -d "${CACHE_DIR}"
  rm -f "${tmp_zip}"
  chmod +x "${GODOT_BIN}"
fi

ln -sf "${GODOT_BIN}" /usr/local/bin/godot 2>/dev/null || true
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  echo "export GODOT=\"${GODOT_BIN}\"" >> "${CLAUDE_ENV_FILE}"
fi

# First import builds .godot/ (class cache, imported textures); the tests
# need it. Idempotent and quick once the cache exists.
cd "${CLAUDE_PROJECT_DIR:-$(pwd)}"
"${GODOT_BIN}" --headless --path . --import < /dev/null > /dev/null 2>&1 || true
"${GODOT_BIN}" --version
