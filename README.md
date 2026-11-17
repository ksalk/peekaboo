# Peekaboo

A simple, lightweight, self-hosted RTSP camera viewer for the web.

Peekaboo shows your cameras in a grid and only pulls a stream when you explicitly
press **Start**. Feeds are relayed through the server (browsers cannot play RTSP
directly) and every stream is stopped the moment you stop watching.

> Work in progress — setup and configuration docs land with the first usable
> release.

## Stack

- **Backend:** ASP.NET Core minimal API, SQLite, ffmpeg
- **Frontend:** Svelte + TypeScript
- **Toolchain:** [mise](https://mise.jdx.dev/)
- **Delivery:** Docker image

## License

MIT — see [LICENSE](LICENSE).
