#!/bin/bash
# Sets up a web session to build and test the C# project: installs (once,
# cached) the .NET SDK and the official Godot 4.7.2 .NET (mono) Linux build,
# builds the C# assembly, and imports the project so the headless tests can
# run straight away:
#   godot --headless --path . --script res://src/Tests/PopulationEngineTest.cs < /dev/null
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

GODOT_VERSION="4.7.2-stable"
GODOT_DIR_NAME="Godot_v${GODOT_VERSION}_mono_linux_x86_64"
GODOT_NAME="Godot_v${GODOT_VERSION}_mono_linux.x86_64"
# This repo's assets-v1 release mirrors the official zip byte for byte;
# upstream is the fallback.
GODOT_URLS=(
  "https://github.com/supa-bit/Facsimilia/releases/download/assets-v1/${GODOT_DIR_NAME}.zip"
  "https://github.com/godotengine/godot/releases/download/${GODOT_VERSION}/${GODOT_DIR_NAME}.zip"
)
CACHE_DIR="${HOME}/.cache/godot/${GODOT_VERSION}-mono"
GODOT_BIN="${CACHE_DIR}/${GODOT_DIR_NAME}/${GODOT_NAME}"
DOTNET_CHANNEL="10.0"
DOTNET_DIR="${HOME}/.dotnet"

retry_download() {  # retry_download <output> <url>...
  local out="$1"; shift
  for delay in 2 4 8 16 0; do
    for url in "$@"; do
      if curl -fsSL --retry 2 -o "${out}" "${url}"; then return 0; fi
    done
    [ "${delay}" -eq 0 ] && break
    sleep "${delay}"
  done
  return 1
}

if ! "${DOTNET_DIR}/dotnet" --list-sdks 2>/dev/null | grep -q "^${DOTNET_CHANNEL}\."; then
  installer="$(mktemp)"
  retry_download "${installer}" https://dot.net/v1/dotnet-install.sh || { echo ".NET installer download failed" >&2; exit 1; }
  bash "${installer}" --channel "${DOTNET_CHANNEL}" --install-dir "${DOTNET_DIR}" > /dev/null
  rm -f "${installer}"
fi
export DOTNET_ROOT="${DOTNET_DIR}" PATH="${DOTNET_DIR}:${PATH}" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

if [ ! -x "${GODOT_BIN}" ]; then
  mkdir -p "${CACHE_DIR}"
  tmp_zip="$(mktemp "${CACHE_DIR}/godot.XXXXXX.zip")"
  retry_download "${tmp_zip}" "${GODOT_URLS[@]}" || { rm -f "${tmp_zip}"; echo "Godot download failed" >&2; exit 1; }
  unzip -o -q "${tmp_zip}" -d "${CACHE_DIR}"
  rm -f "${tmp_zip}"
  chmod +x "${GODOT_BIN}"
fi

ln -sf "${GODOT_BIN}" /usr/local/bin/godot 2>/dev/null || true
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo "export GODOT=\"${GODOT_BIN}\""
    echo "export DOTNET_ROOT=\"${DOTNET_DIR}\""
    echo "export PATH=\"${DOTNET_DIR}:\$PATH\""
    echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1"
  } >> "${CLAUDE_ENV_FILE}"
fi

# Build the C# assembly, then import: the first import builds .godot/
# (class cache, imported textures), which the tests need. Idempotent and
# quick once the caches exist.
cd "${CLAUDE_PROJECT_DIR:-$(pwd)}"
dotnet build -v q -nologo > /dev/null
"${GODOT_BIN}" --headless --path . --import < /dev/null > /dev/null 2>&1 || true
"${GODOT_BIN}" --version
