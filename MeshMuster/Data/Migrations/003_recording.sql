-- The flash log. A row lands only when a version actually changed, and "last updated" is the
-- newest occurred_at here rather than a column that can drift.
CREATE TABLE device_flashes (
    id                    TEXT PRIMARY KEY,
    device_id             TEXT NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
    occurred_at           TEXT NOT NULL,
    firmware_from_label   TEXT NOT NULL DEFAULT '',
    firmware_to_label     TEXT NOT NULL DEFAULT '',
    bootloader_from_label TEXT NOT NULL DEFAULT '',
    bootloader_to_label   TEXT NOT NULL DEFAULT '',
    note                  TEXT NOT NULL DEFAULT '',
    created_at            TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX ix_device_flashes_device ON device_flashes (device_id, occurred_at DESC);
