#!/usr/bin/env bash
set -Eeuo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
root_dir="$PWD"
isapi_port=8666

for tool in dotnet node npm ffmpeg curl tar ps; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "Missing required tool: $tool" >&2
    exit 1
  fi
done

for port in 5080 5173 8555 "$isapi_port"; do
  if (exec 3<>"/dev/tcp/127.0.0.1/${port}") 2>/dev/null; then
    echo "Port ${port} is already in use. Stop 'mise dev' (or other services) first." >&2
    exit 1
  fi
done

temp_dir="$(mktemp -d "${TMPDIR:-/tmp}/peekaboo-e2e.XXXXXX")"
web_pid=""
api_pid=""
feed1_pid=""
feed2_pid=""
feed3_pid=""
playback_feed_pid=""
fake_isapi_pid=""
mtx_pid=""

cleanup() {
  for pid in "$web_pid" "$api_pid" "$playback_feed_pid" "$feed3_pid" "$feed2_pid" "$feed1_pid" "$fake_isapi_pid" "$mtx_pid"; do
    if [ -n "$pid" ]; then
      kill -TERM -- "-$pid" 2>/dev/null || true
    fi
  done

  for pid in "$web_pid" "$api_pid" "$playback_feed_pid" "$feed3_pid" "$feed2_pid" "$feed1_pid" "$fake_isapi_pid" "$mtx_pid"; do
    if [ -n "$pid" ]; then
      wait "$pid" 2>/dev/null || true
    fi
  done

  rm -rf e2e/.auth

  if [ "${PEEKABOO_E2E_KEEP:-0}" != "1" ]; then
    rm -rf "$temp_dir"
  else
    echo "E2E logs kept in ${temp_dir}" >&2
  fi
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

wait_for_port() {
  local port="$1"
  for _ in $(seq 1 120); do
    if (exec 3<>"/dev/tcp/127.0.0.1/${port}") 2>/dev/null; then
      return 0
    fi
    sleep 0.25
  done
  return 1
}

wait_for_http() {
  local url="$1"
  for _ in $(seq 1 120); do
    if curl -fsS "$url" >/dev/null 2>&1; then
      return 0
    fi
    sleep 0.25
  done
  return 1
}

wait_for_feed() {
  local path="$1"
  for _ in $(seq 1 120); do
    if grep -q "path ${path}] stream is available" "$temp_dir/mediamtx.log" 2>/dev/null; then
      return 0
    fi
    sleep 0.25
  done
  return 1
}

dump_feed_logs() {
  local log
  for log in "$temp_dir"/feed*.log "$temp_dir"/playback-feed.log; do
    [ -f "$log" ] || continue
    echo "--- $(basename "$log") ---" >&2
    tail -20 "$log" >&2 || true
  done
}

start_feed() {
  local path="$1"
  local hue="$2"
  local log="$3"
  local width="${4:-640}"
  local height="${5:-360}"
  local audio="${6:-0}"
  local audio_args=()

  if [ "$audio" = "1" ]; then
    audio_args=(
      -re -f lavfi -i "sine=frequency=440:sample_rate=48000"
      -map 0:v:0 -map 1:a:0
      -c:a pcm_mulaw -ar 8000 -ac 1
    )
  else
    audio_args=(-an)
  fi

  ffmpeg -hide_banner -loglevel warning -re \
    -f lavfi -i "testsrc2=size=${width}x${height}:rate=15" \
    "${audio_args[@]}" \
    -vf "hue=h=${hue}" \
    -c:v libx264 -preset ultrafast -tune zerolatency -g 15 -pix_fmt yuv420p \
    -rtsp_transport tcp -f rtsp \
    "rtsp://camuser:campassword@127.0.0.1:8555/${path}" \
    >"$log" 2>&1 &

  last_pid=$!
}

set -m

mtx_bin="$(./scripts/get-mediamtx.sh)"
(
  cd "$temp_dir"
  exec "$mtx_bin" "$root_dir/e2e/mediamtx.yml"
) >"$temp_dir/mediamtx.log" 2>&1 &
mtx_pid=$!

if ! wait_for_port 8555; then
  echo "MediaMTX did not start." >&2
  tail -20 "$temp_dir/mediamtx.log" >&2 || true
  exit 1
fi

start_feed "ISAPI/Streaming/channels/101" 0 "$temp_dir/feed1.log" 640 360
feed1_pid="$last_pid"
start_feed "ISAPI/Streaming/channels/102" 120 "$temp_dir/feed2.log" 320 180
feed2_pid="$last_pid"
start_feed "ISAPI/Streaming/channels/201" 240 "$temp_dir/feed3.log" 640 360 1
feed3_pid="$last_pid"
start_feed "ISAPI/Streaming/tracks/101" 300 "$temp_dir/playback-feed.log" 640 360 1
playback_feed_pid="$last_pid"

if ! wait_for_feed "ISAPI/Streaming/channels/101" ||
   ! wait_for_feed "ISAPI/Streaming/channels/102" ||
   ! wait_for_feed "ISAPI/Streaming/channels/201" ||
   ! wait_for_feed "ISAPI/Streaming/tracks/101"; then
  echo "Test RTSP feeds did not come online." >&2
  tail -20 "$temp_dir/mediamtx.log" >&2 || true
  dump_feed_logs
  exit 1
fi

if [ ! -d frontend/node_modules ]; then
  npm --prefix frontend ci
fi

if [ ! -d e2e/node_modules ]; then
  npm --prefix e2e ci
fi

dotnet build backend/Peekaboo.slnx --nologo

CAMERA_USERNAME="camuser" \
CAMERA_PASSWORD="campassword" \
PEEKABOO_FAKE_ISAPI_PORT="$isapi_port" \
node "$root_dir/e2e/fake-isapi.mjs" \
  >"$temp_dir/isapi.log" 2>&1 &
fake_isapi_pid=$!

if ! wait_for_port "$isapi_port"; then
  echo "Fake NVR ISAPI server did not start." >&2
  tail -30 "$temp_dir/isapi.log" >&2 || true
  exit 1
fi

PEEKABOO_DB_PATH="$temp_dir/peekaboo.db" \
ADMIN_USERNAME="admin" \
ADMIN_PASSWORD="peekaboo-e2e-password" \
CAMERA_USERNAME="camuser" \
CAMERA_PASSWORD="campassword" \
PEEKABOO_ISAPI_HTTP_PORT="$isapi_port" \
ASPNETCORE_ENVIRONMENT="Development" \
ASPNETCORE_URLS="http://127.0.0.1:5080" \
dotnet run --no-build --project backend/Peekaboo.Api --no-launch-profile \
  >"$temp_dir/api.log" 2>&1 &
api_pid=$!

if ! wait_for_http "http://127.0.0.1:5080/api/health"; then
  echo "Peekaboo API did not start." >&2
  tail -30 "$temp_dir/api.log" >&2 || true
  exit 1
fi

npm --prefix frontend run dev -- --host 127.0.0.1 --port 5173 --strictPort >"$temp_dir/web.log" 2>&1 &
web_pid=$!

if ! wait_for_http "http://127.0.0.1:5173/"; then
  echo "Vite did not start." >&2
  tail -30 "$temp_dir/web.log" >&2 || true
  exit 1
fi

export PEEKABOO_E2E_BASE_URL="http://127.0.0.1:5173"
export PEEKABOO_E2E_ADMIN_USERNAME="admin"
export PEEKABOO_E2E_ADMIN_PASSWORD="peekaboo-e2e-password"

set +e
npm --prefix e2e test -- "$@"
status=$?
set -e

if [ "$status" -ne 0 ]; then
  echo "--- api.log ---" >&2
  tail -40 "$temp_dir/api.log" >&2 || true
  echo "--- mediamtx.log ---" >&2
  tail -40 "$temp_dir/mediamtx.log" >&2 || true
  echo "--- isapi.log ---" >&2
  tail -40 "$temp_dir/isapi.log" >&2 || true
  echo "--- web.log ---" >&2
  tail -20 "$temp_dir/web.log" >&2 || true
  echo "--- feed logs ---" >&2
  dump_feed_logs
  mkdir -p e2e/test-results
  cp "$temp_dir"/*.log e2e/test-results/ 2>/dev/null || true
fi

exit "$status"
