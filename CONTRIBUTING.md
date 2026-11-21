# Contributing

Thanks for taking a look at Peekaboo. The project intentionally stays small: a
single ASP.NET Core service, a Svelte frontend, SQLite, and ffmpeg.

## Development setup

```bash
mise install
mise run setup:web
mise run dev
```

Open <http://localhost:5173>. The dev admin is `admin` / `peekaboo-dev-password`
(defined in `scripts/dev.sh`; do not reuse it in production).

## Before opening a pull request

```bash
mise run lint
mise run test:api
mise run e2e
```

`mise run test` runs the backend and end-to-end suites. The e2e suite needs ffmpeg
and Chrome/Chromium with H.264 support, and uses ports 8555, 5080 and 5173.

## Guidelines

- Keep the core promise intact: no stream may start without an explicit viewer, and
  every stop path must terminate the server-side pull.
- Prefer the simplest solution that preserves that guarantee. There is no
  transcoding, recording, or background polling.
- Add or update tests for behaviour changes. Streaming lifecycle changes should be
  covered by the Playwright suite.
- Never commit secrets, `.env` files, databases, generated certificates, or
  `mise.local.toml`.
- Never commit the proprietary Hikvision SDK. It belongs in the gitignored
  `vendor/hikvision` directory and only in the optional private `runtime-sdk` image.
  Real-NVR tests are opt-in through `PEEKABOO_HIK_INTEGRATION`.
- Keep commits focused and use one-line messages in the existing style
  (`feat: ...`, `fix: ...`, `test: ...`, `docs: ...`).

## Reporting security issues

See [SECURITY.md](SECURITY.md). Do not open a public issue for a vulnerability.
