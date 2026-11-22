# Releasing

Peekaboo uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html) and
annotated git tags named `vMAJOR.MINOR.PATCH`.

`VERSION` at the repository root is the single source of truth. The .NET build reads
it through `Directory.Build.props`, the frontend reads it from `vite.config.ts`, and
`mise run docker:build` uses it as the default image tag.

## Version policy

- While the version is `0.x`, `MINOR` releases may contain breaking changes and
  `PATCH` releases contain fixes only.
- Use `1.0.0` when the configuration format, database migrations, and HTTP API are
  stable enough to promise compatibility.
- Prereleases use a SemVer suffix, for example `0.2.0-rc.1`.

The application version is separate from the SQLite schema version. The database
schema is tracked by `PRAGMA user_version` and the migration runner; mention schema
changes in the changelog entry.

## Release checklist

1. Update `VERSION`, for example to `0.2.0`.
2. Add a `## [0.2.0] - YYYY-MM-DD` section to `CHANGELOG.md`.
3. Commit both files:

   ```bash
   git add VERSION CHANGELOG.md
   git commit -m "chore(release): 0.2.0"
   ```

4. Create the annotated tag:

   ```bash
   mise run release
   ```

   The release script checks that the tree is clean, the current branch is `main`,
   `VERSION` is valid SemVer, the changelog section exists, and the tag is new.

5. Push the commit and tag:

   ```bash
   git push origin main
   git push origin v0.2.0
   ```

6. Publish the container image:

   ```bash
   mise run docker:publish
   ```

   This publishes `peekaboo:0.2.0` to `PEEKABOO_REGISTRY`. The image is labelled with
   the OCI version and the short Git revision.

## Upgrading a deployment

Pin the exact release in `.env`:

```env
PEEKABOO_IMAGE=registry.example.com/peekaboo:0.2.0
```

Then:

```bash
docker compose pull
docker compose up -d
```

Back up the `peekaboo-data` volume before upgrading. Database migrations run
automatically on startup; a build refuses to open a database written by a newer
schema version, so rolling back to an older image may require restoring the backup.
