# Peekaboo

A simple, lightweight, self-hosted RTSP camera viewer for the web.

![Peekaboo showing a test feed in a 1×1 grid](docs/screenshot.png)

Peekaboo is built around one rule: **a camera is only pulled while someone is
actually watching it**. Nothing is streamed on page load, and the server kills the
RTSP/ffmpeg process as soon as the last viewer stops watching. Closing the tab,
changing the grid, logging out, or losing the connection all stop the stream.

## Features

- **Explicit streaming** — feeds start only when you press **Start** and stop on
  **Stop**, tab hide, navigation, logout, or disconnect.
- **Bandwidth-aware** — one shared RTSP pull per camera even if several browsers or
  grid cells watch it; video only (audio is discarded); configurable caps.
- **Grid layouts** — 1×1, 2×2 and 3×3; click a cell to assign a camera. Layouts and
  assignments are remembered per device and never auto-start.
- **Camera management** — name cameras and store credential-free RTSP URLs in SQLite.
- **Accounts** — cookie-based login, PBKDF2 password hashes, admin-managed users.
  No public registration.
- **Self-hosted** — single Docker image, SQLite persistence, no external services.
- **Simple UI** — Svelte + TypeScript, responsive down to a phone screen.

## How streaming works

Browsers cannot play RTSP. Peekaboo relays each feed through ffmpeg:

```text
RTSP camera ──ffmpeg (copy)──▶ fragmented MP4 ──WebSocket──▶ browser MediaSource
                 │
                 └── started by the first viewer, killed with the last
```

- `ffmpeg` copies the H.264 video stream (`-c:v copy`), so the server does not
  transcode and CPU usage stays low.
- The browser receives `ftyp`/`moov`/`moof`/`mdat` boxes over WebSocket and feeds
  them to a `MediaSource`.
- Streams are refcounted per camera. Two cells watching the same camera share one
  upstream pull.
- A 30-second watchdog kills stalled pulls; `PEEKABOO_MAX_STREAMS` caps concurrent
  cameras and `PEEKABOO_MAX_VIEWERS_PER_STREAM` caps viewers per camera.

## Quick start (Docker)

```bash
cp .env.example .env
# Edit .env: set ADMIN_PASSWORD and, if your cameras require auth,
# CAMERA_USERNAME and CAMERA_PASSWORD.

docker compose up -d --build
```

Open <http://localhost:8080> and sign in with the admin account from `.env`.

The initial admin is created **only while the database has no users**. Later
`ADMIN_PASSWORD` changes do not reset it; use the Users page or delete the data
volume to start over.

### Using a published image

```bash
# .env
PEEKABOO_IMAGE=registry.example.com/peekaboo:0.1.0
```

```bash
docker compose pull
docker compose up -d
```

### Data

SQLite lives in the `peekaboo-data` volume at `/data/peekaboo.db`. To back it
up, stop the container and copy the whole data directory to the host, for example:

```bash
docker compose stop peekaboo
docker compose cp peekaboo:/data ./peekaboo-data-backup
docker compose start peekaboo
```

You can then run `sqlite3 .backup` on the copied database on the host.
`docker compose down -v` deletes all users, sessions and cameras.

## Cameras

In **Cameras**, add a name and an RTSP URL such as:

```text
rtsp://192.168.1.10:554/stream1
```

Do not put credentials in the URL — Peekaboo rejects them. Instead set one global
pair on the server:

```env
CAMERA_USERNAME=viewer
CAMERA_PASSWORD=secret
```

Credentials are injected into the RTSP URL only at stream start. They never reach
the browser and are never written to SQLite.

> **Use the camera's H.264 substream when possible.** It uses less bandwidth and
> decoding power, and H.264 is the only codec Peekaboo currently relays. H.265-only
> streams are rejected with a clear error.

## Usage

1. Add cameras under **Cameras**.
2. Open **Feeds**, choose a grid layout, and click a cell to pick a camera.
3. Press **Start** in the cell. Press **Stop** when you are done.
4. Use **Stop all** to release every camera at once.

Assignments are stored per layout in this browser's `localStorage`; they survive
reloads but never start streams automatically. Deleting a camera clears its cell the
next time the feed view loads.

Admins can create and delete users and reset passwords under **Users**. Every user
can change their own password under **Account**.

## Configuration

| Variable | Default | Purpose |
| --- | --- | --- |
| `ADMIN_USERNAME` | — | Initial admin username; required only on first run. |
| `ADMIN_PASSWORD` | — | Initial admin password (8–256 chars); required only on first run. |
| `CAMERA_USERNAME` | empty | Global camera username. Set together with `CAMERA_PASSWORD`. |
| `CAMERA_PASSWORD` | empty | Global camera password. |
| `PEEKABOO_DB_PATH` | `data/peekaboo.db` (`/data/peekaboo.db` in Docker) | SQLite file path. |
| `PEEKABOO_MAX_STREAMS` | `9` | Maximum simultaneously active cameras (1–64). |
| `PEEKABOO_MAX_VIEWERS_PER_STREAM` | `8` | Maximum WebSocket viewers per camera (1–64). |
| `PEEKABOO_RTSP_TRANSPORT` | `tcp` | `tcp` or `udp`. TCP is recommended and more reliable. |
| `PEEKABOO_TRUST_PROXY` | `false` | Trust `X-Forwarded-*` headers from a reverse proxy. |
| `PEEKABOO_FFMPEG_PATH` | `ffmpeg` | ffmpeg binary; mainly useful in development. |
| `PEEKABOO_PORT` | `8080` | Host port published by `docker-compose.yml`. |
| `PEEKABOO_IMAGE` | `peekaboo:local` | Image used by `docker-compose.yml`. |

Invalid stream limits or a half-configured camera credential pair fail fast at
startup with an explanatory error.

## Reverse proxy and HTTPS

Put Peekaboo behind TLS for any non-local deployment. A minimal nginx location:

```nginx
location / {
    proxy_pass http://127.0.0.1:8080;
    proxy_http_version 1.1;
    proxy_set_header Host $http_host;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;

    # WebSocket upgrade for camera streams
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_read_timeout 1h;
}
```

Then set `PEEKABOO_TRUST_PROXY=true` so the app sees the real client IP, uses
`Secure` session cookies, and validates the forwarded host correctly. Only enable
this when the container port is reachable **exclusively** through the proxy;
otherwise forwarded headers can be spoofed.

## Development

Prerequisites: [mise](https://mise.jdx.dev/), ffmpeg, and (for end-to-end tests) a
Chrome/Chromium build with H.264 support.

```bash
mise install
mise run setup:web
mise run dev
```

- UI: <http://localhost:5173>
- API: <http://localhost:5080>
- Dev admin: `admin` / `peekaboo-dev-password` (from `scripts/dev.sh`; never use
  these defaults in production)

Useful tasks:

| Task | What it does |
| --- | --- |
| `mise run dev` | Backend + Vite dev server. |
| `mise run build` | Build backend and frontend. |
| `mise run test:api` | Backend unit/integration tests. |
| `mise run lint` | ESLint, svelte-check, and e2e type-check. |
| `mise run e2e` | Full Playwright suite with synthetic authenticated RTSP feeds. |
| `mise run test` | Backend tests, then e2e. |
| `mise run docker:build` | Build the image tagged for `PEEKABOO_REGISTRY`. |
| `mise run docker:publish` | Push that image. |

### End-to-end tests

`mise run e2e` starts MediaMTX (downloaded, checksum-verified and cached under
`e2e/.cache`), pushes two synthetic H.264 feeds with ffmpeg, starts the app with a
fresh SQLite database, and drives Chromium with Playwright. It verifies real video
decoding (a pixel diff), shared streams, and every stop path by counting actual
ffmpeg processes on the host. Ports 5080, 5173 and 8555 must be free.

The suite is Linux/macOS oriented because it inspects the process table. It is run
in CI on GitHub-hosted Ubuntu with Google Chrome.

## Publishing images

Docker builds and pushes are intentionally local; CI only tests.

```bash
cp mise.local.example.toml mise.local.toml
# set PEEKABOO_REGISTRY and PEEKABOO_TAG in mise.local.toml (gitignored)

docker login registry.example.com
mise run docker:publish
```

Registry credentials stay in Docker's credential store. The registry URL and tag
stay in `mise.local.toml`, which is gitignored.

## Security

- Session cookies are `HttpOnly`, `SameSite=Lax`, and `Secure` when served over
  HTTPS.
- Passwords use PBKDF2-SHA256 (210,000 iterations) with per-password salts.
- Login attempts are rate-limited per client IP.
- WebSocket upgrades require a session and a same-host `Origin`.
- CSP, `X-Content-Type-Options`, `X-Frame-Options` and `Referrer-Policy` headers are
  set by the server.
- Camera credentials live only in the server environment and the short-lived ffmpeg
  process arguments. They are not stored in SQLite or sent to the browser.
- ffmpeg is started with an argument list (no shell), and camera URLs are restricted
  to `rtsp://`/`rtsps://` with embedded credentials rejected.

Operational notes:

- Use HTTPS, a strong admin password, and a dedicated camera account.
- The SQLite volume contains password hashes and camera URLs; protect and back it
  up accordingly.
- Peekaboo is designed for a trusted self-hosted network. It is not multi-tenant.

See [SECURITY.md](SECURITY.md) for reporting a vulnerability.

## Limitations

- H.264 only; no transcoding or H.265 support.
- Video only; audio is intentionally dropped to save bandwidth.
- 1–3 second latency (depends on the camera GOP/encoder settings).
- One app instance per database; streaming state is in-process and not clustered.
- No recording, motion detection, PTZ controls, or automatic reconnect (reconnect is
  deliberately manual so a stopped stream cannot start itself).
- One global camera credential pair for now.
- Grid assignments are per browser/device, not per account.

## License

MIT — see [LICENSE](LICENSE).
