ALTER TABLE nvrs ADD COLUMN transport TEXT NOT NULL DEFAULT 'rtsp'
    CHECK (transport IN ('rtsp', 'sdk'));
ALTER TABLE nvrs ADD COLUMN sdk_port INTEGER NOT NULL DEFAULT 8000
    CHECK (sdk_port BETWEEN 1 AND 65535);
