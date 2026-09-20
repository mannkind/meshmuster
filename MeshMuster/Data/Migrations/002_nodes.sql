CREATE TABLE devices (
    id                       TEXT PRIMARY KEY,
    name                     TEXT NOT NULL,
    role                     TEXT NOT NULL,                -- repeater|companion|room|sensor|kiss
    board_id                 TEXT NULL REFERENCES boards(id) ON DELETE SET NULL,

    -- The stream is worked out on save, so it can never disagree with the role. The release id
    -- is null when we don't know the exact build; the label is always set and is what shows.
    firmware_stream_id       TEXT NULL REFERENCES streams(id) ON DELETE SET NULL,
    firmware_release_id      TEXT NULL REFERENCES releases(id) ON DELETE SET NULL,
    firmware_version_label   TEXT NOT NULL DEFAULT '',
    firmware_sort_key        TEXT NOT NULL DEFAULT '',

    bootloader_stream_id     TEXT NULL REFERENCES streams(id) ON DELETE SET NULL,
    bootloader_release_id    TEXT NULL REFERENCES releases(id) ON DELETE SET NULL,
    bootloader_version_label TEXT NOT NULL DEFAULT '',
    bootloader_sort_key      TEXT NOT NULL DEFAULT '',

    -- Both or neither. Half a pair would drop a marker on the equator.
    latitude                 REAL NULL,
    longitude                REAL NULL,

    private_key              TEXT NOT NULL DEFAULT '',
    admin_password           TEXT NOT NULL DEFAULT '',
    notes                    TEXT NOT NULL DEFAULT '',
    is_active                INTEGER NOT NULL DEFAULT 1,
    created_at               TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at               TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX ix_devices_role ON devices (role);
CREATE INDEX ix_devices_firmware_sort ON devices (firmware_sort_key);
CREATE INDEX ix_devices_bootloader_sort ON devices (bootloader_sort_key);
