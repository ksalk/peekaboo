#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://127.0.0.1:5080}"
export PEEKABOO_DB_PATH="${PEEKABOO_DB_PATH:-$PWD/data/peekaboo.db}"
export ADMIN_USERNAME="${ADMIN_USERNAME:-admin}"
export ADMIN_PASSWORD="${ADMIN_PASSWORD:-peekaboo-dev-password}"

if [[ -n "${PEEKABOO_HIK_SDK_PATH:-}" ]]; then
  export LD_LIBRARY_PATH="${PEEKABOO_HIK_SDK_PATH}:${PEEKABOO_HIK_SDK_PATH}/HCNetSDKCom${LD_LIBRARY_PATH:+:${LD_LIBRARY_PATH}}"
fi

echo "Peekaboo dev: http://127.0.0.1:5173 (API http://127.0.0.1:5080)"
echo "Admin login: ${ADMIN_USERNAME} / ${ADMIN_PASSWORD}"

set -m

dotnet run --project backend/Peekaboo.Api --no-launch-profile &
api_pid=$!

npm --prefix frontend run dev &
web_pid=$!

cleanup() {
  for pid in "$api_pid" "$web_pid"; do
    kill -TERM -- "-$pid" 2>/dev/null || true
  done
  wait "$api_pid" "$web_pid" 2>/dev/null || true
}
trap cleanup EXIT INT TERM

wait -n "$api_pid" "$web_pid"
