-- The upstream repos we poll for releases.
CREATE TABLE sources (
    id                  TEXT PRIMARY KEY,
    slug                TEXT NOT NULL UNIQUE,
    kind                TEXT NOT NULL,                     -- 'firmware' | 'bootloader'
    display_name        TEXT NOT NULL,
    github_owner        TEXT NOT NULL,
    github_repo         TEXT NOT NULL,
    include_prereleases INTEGER NOT NULL DEFAULT 0,
    is_enabled          INTEGER NOT NULL DEFAULT 1,
    etag                TEXT NOT NULL DEFAULT '',
    last_polled_at      TEXT NULL,
    last_poll_error     TEXT NOT NULL DEFAULT '',
    sort_order          INTEGER NOT NULL DEFAULT 0,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at          TEXT NOT NULL DEFAULT (datetime('now'))
);

-- A release channel inside a source. A device's role picks which stream it follows; a null
-- device_role means the stream serves every role, which is how the bootloader repos work.
CREATE TABLE streams (
    id           TEXT PRIMARY KEY,
    source_id    TEXT NOT NULL REFERENCES sources(id) ON DELETE CASCADE,
    slug         TEXT NOT NULL,
    display_name TEXT NOT NULL,
    device_role  TEXT NULL,                                -- a role below, or NULL for any
    UNIQUE (source_id, slug)
);
CREATE INDEX ix_streams_source ON streams (source_id);

CREATE TABLE releases (
    id            TEXT PRIMARY KEY,
    stream_id     TEXT NOT NULL REFERENCES streams(id) ON DELETE CASCADE,
    github_id     INTEGER NOT NULL,
    tag           TEXT NOT NULL,
    name          TEXT NOT NULL DEFAULT '',
    version_label TEXT NOT NULL,
    -- Four components, five digits each, worked out once at sync time. Sorting is then an
    -- indexed column scan instead of a comparison per request.
    sort_key      TEXT NOT NULL,
    detail        TEXT NOT NULL DEFAULT '',
    is_prerelease INTEGER NOT NULL DEFAULT 0,
    published_at  TEXT NOT NULL,
    html_url      TEXT NOT NULL DEFAULT '',
    first_seen_at TEXT NOT NULL DEFAULT (datetime('now')),
    UNIQUE (stream_id, github_id)
);
CREATE INDEX ix_releases_stream_sort ON releases (stream_id, sort_key DESC);

CREATE TABLE release_assets (
    id           TEXT PRIMARY KEY,
    release_id   TEXT NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    name         TEXT NOT NULL,
    download_url TEXT NOT NULL,
    size_bytes   INTEGER NOT NULL DEFAULT 0,
    UNIQUE (release_id, name)
);
CREATE INDEX ix_release_assets_release ON release_assets (release_id);

CREATE TABLE boards (
    id          TEXT PRIMARY KEY,
    name        TEXT NOT NULL UNIQUE,
    description TEXT NOT NULL DEFAULT '',
    is_active   INTEGER NOT NULL DEFAULT 1,
    created_at  TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at  TEXT NOT NULL DEFAULT (datetime('now'))
);

-- Which filename in a given source's releases belongs to this board.
CREATE TABLE board_asset_patterns (
    id        TEXT PRIMARY KEY,
    board_id  TEXT NOT NULL REFERENCES boards(id) ON DELETE CASCADE,
    source_id TEXT NOT NULL REFERENCES sources(id) ON DELETE CASCADE,
    pattern   TEXT NOT NULL,
    UNIQUE (board_id, source_id)
);
CREATE INDEX ix_board_asset_patterns_board ON board_asset_patterns (board_id);

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

-- The four repos and their streams, with fixed ids so the import can name them by value. Tag
-- syntax lives in Services/Versioning; this only says which streams exist and what each serves.
--
-- Prereleases are on for both forks on purpose, because every mikecarper release is flagged
-- prerelease and filtering them out would leave that channel looking empty.

INSERT INTO sources (id, slug, kind, display_name, github_owner, github_repo,
                     include_prereleases, is_enabled, sort_order)
VALUES
  ('a1000000-0000-4000-8000-000000000001', 'meshcore-official',   'firmware',
   'MeshCore (official)',   'meshcore-dev', 'MeshCore', 0, 1, 1),
  ('a1000000-0000-4000-8000-000000000002', 'meshcore-mikecarper', 'firmware',
   'MeshCore (mikecarper)', 'mikecarper',   'MeshCore', 1, 1, 2),
  ('a1000000-0000-4000-8000-000000000003', 'otafix-oltaco',       'bootloader',
   'OTAFIX (oltaco)',       'oltaco',       'Adafruit_nRF52_Bootloader_OTAFIX', 1, 1, 3),
  ('a1000000-0000-4000-8000-000000000004', 'otafix-mikecarper',   'bootloader',
   'OTAFIX (mikecarper)',   'mikecarper',   'Adafruit_nRF52_Bootloader_OTAFIX', 1, 1, 4);

-- Both firmware sources have a stream for every role. The official Sensor and KISS streams
-- can be empty; keeping them lets a device retain its selected source until releases arrive.
--
-- mikecarper ships repeater and room firmware under a single repeater-room-v tag. They are
-- still two streams, because a device follows one role and should never be offered the other's
-- firmware; the tag parser files such a release into both.

INSERT INTO streams (id, source_id, slug, display_name, device_role)
VALUES
  ('b1000000-0000-4000-8000-000000000001', 'a1000000-0000-4000-8000-000000000001',
   'repeater',  'Repeater',    'repeater'),
  ('b1000000-0000-4000-8000-000000000002', 'a1000000-0000-4000-8000-000000000001',
   'companion', 'Companion',   'companion'),
  ('b1000000-0000-4000-8000-000000000003', 'a1000000-0000-4000-8000-000000000002',
   'repeater',  'Repeater',    'repeater'),
  ('b1000000-0000-4000-8000-000000000004', 'a1000000-0000-4000-8000-000000000002',
   'companion', 'Companion',   'companion'),
  ('b1000000-0000-4000-8000-000000000005', 'a1000000-0000-4000-8000-000000000003',
   'default',   'Bootloader',  NULL),
  ('b1000000-0000-4000-8000-000000000006', 'a1000000-0000-4000-8000-000000000004',
   'default',   'Bootloader',  NULL),
  ('b1000000-0000-4000-8000-000000000007', 'a1000000-0000-4000-8000-000000000001',
   'room',      'Room server', 'room'),
  ('b1000000-0000-4000-8000-000000000008', 'a1000000-0000-4000-8000-000000000002',
   'room',      'Room',        'room'),
  ('b1000000-0000-4000-8000-000000000009', 'a1000000-0000-4000-8000-000000000002',
   'sensor',    'Sensor',      'sensor'),
  ('b1000000-0000-4000-8000-000000000010', 'a1000000-0000-4000-8000-000000000002',
   'kiss',      'KISS',        'kiss'),
  ('b1000000-0000-4000-8000-000000000011', 'a1000000-0000-4000-8000-000000000001',
   'sensor',    'Sensor',      'sensor'),
  ('b1000000-0000-4000-8000-000000000012', 'a1000000-0000-4000-8000-000000000001',
   'kiss',      'KISS',        'kiss');
