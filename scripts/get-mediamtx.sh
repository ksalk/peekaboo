#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

version="${MEDIAMTX_VERSION:-v1.21.4}"

case "$(uname -s)" in
  Linux) os="linux" ;;
  Darwin) os="darwin" ;;
  *)
    echo "Unsupported operating system for MediaMTX download." >&2
    exit 1
    ;;
esac

case "$(uname -m)" in
  x86_64) arch="amd64" ;;
  aarch64 | arm64) arch="arm64" ;;
  *)
    echo "Unsupported architecture for MediaMTX download." >&2
    exit 1
    ;;
esac

cache_dir="${PEEKABOO_E2E_CACHE_DIR:-e2e/.cache}"
destination="${cache_dir}/mediamtx-${version}-${os}-${arch}"

if [ -x "$destination" ]; then
  realpath "$destination"
  exit 0
fi

asset="mediamtx_${version}_${os}_${arch}.tar.gz"
base_url="https://github.com/bluenviron/mediamtx/releases/download/${version}"
temp_dir="$(mktemp -d)"
trap 'rm -rf "$temp_dir"' EXIT

echo "Downloading MediaMTX ${version}..." >&2
curl -fsSL --retry 3 --retry-delay 1 -o "$temp_dir/checksums.sha256" "${base_url}/checksums.sha256"
curl -fsSL --retry 3 --retry-delay 1 -o "$temp_dir/mediamtx.tar.gz" "${base_url}/${asset}"

expected_hash="$(awk -v asset="$asset" '{ name = $2; sub(/^\*/, "", name); if (name == asset) print $1 }' "$temp_dir/checksums.sha256")"
if [ -z "$expected_hash" ]; then
  echo "No checksum published for ${asset}." >&2
  exit 1
fi

if command -v sha256sum >/dev/null 2>&1; then
  actual_hash="$(sha256sum "$temp_dir/mediamtx.tar.gz" | awk '{ print $1 }')"
else
  actual_hash="$(shasum -a 256 "$temp_dir/mediamtx.tar.gz" | awk '{ print $1 }')"
fi

if [ "$expected_hash" != "$actual_hash" ]; then
  echo "MediaMTX checksum mismatch for ${asset}." >&2
  exit 1
fi

tar -xzf "$temp_dir/mediamtx.tar.gz" -C "$temp_dir"

mkdir -p "$cache_dir"
install -m 0755 "$temp_dir/mediamtx" "$destination"

realpath "$destination"
