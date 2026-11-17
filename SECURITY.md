# Security policy

Peekaboo is a self-hosted application that exposes live camera feeds. Please treat
security reports responsibly.

## Reporting a vulnerability

Do **not** open a public issue for a suspected vulnerability.

Use GitHub's **Report a vulnerability** / private security advisory feature on the
repository. Include:

- a description of the issue and its impact,
- steps to reproduce (a minimal proof of concept if possible),
- affected version or commit,
- any suggested remediation.

You can expect an acknowledgement as soon as possible, and credit in the advisory
unless you prefer to remain anonymous.

## Supported versions

The latest released version receives security fixes. Older versions may be asked to
upgrade.

## Security model and hardening

Peekaboo is intended for a trusted network and a small number of users. Recommended
hardening:

- Serve it over HTTPS behind a reverse proxy.
- Set `PEEKABOO_TRUST_PROXY=true` only when the app port is unreachable except
  through that proxy.
- Use a long, unique `ADMIN_PASSWORD` and a dedicated camera account.
- Keep the SQLite data volume private; it contains password hashes and camera URLs.
- Publish the container port only to the reverse proxy or your LAN.
- Do not run untrusted code or grant accounts to people you would not give camera
  access to.

## Known non-issues

- **Camera credentials are visible in the ffmpeg process arguments.** This is how
  RTSP authentication is passed to ffmpeg. Credentials are injected only at stream
  start and never stored in the database, browser, or logs. Anyone who can inspect
  container processes already has code execution inside the container, which is
  equivalent to full control of Peekaboo.
- **The health endpoint is unauthenticated.** It returns only `{"status":"ok"}` and
  is intended for container orchestration checks.
- **Users may add arbitrary `rtsp://`/`rtsps://` camera URLs.** This is inherent to a
  viewer configured by authenticated users. Do not give accounts to untrusted
  people.
