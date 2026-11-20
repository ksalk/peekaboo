CREATE TABLE nvrs (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    base_url TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

-- The URL-based camera model is replaced by NVR-scoped ISAPI stream IDs.
DROP TABLE cameras;

CREATE TABLE cameras (
    id TEXT PRIMARY KEY,
    nvr_id TEXT NOT NULL REFERENCES nvrs (id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    main_stream_id TEXT NOT NULL,
    sub_stream_id TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE INDEX idx_cameras_nvr_id ON cameras (nvr_id);
