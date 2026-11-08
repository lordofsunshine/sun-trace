namespace SunTrace.Infrastructure.Storage;

internal static class SqliteSchema
{
    public const int CurrentVersion = 1;

    public const string CreateVersionOne = """
        CREATE TABLE IF NOT EXISTS apps (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            publisher TEXT,
            version TEXT,
            install_date TEXT,
            last_updated TEXT,
            install_location TEXT,
            uninstall_string TEXT,
            display_icon TEXT,
            main_executable TEXT,
            registry_key TEXT,
            signature_publisher TEXT,
            estimated_size INTEGER,
            is_system INTEGER NOT NULL DEFAULT 0,
            source TEXT,
            first_seen TEXT NOT NULL,
            last_seen TEXT NOT NULL,
            present INTEGER NOT NULL DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS components (
            id TEXT PRIMARY KEY,
            app_id TEXT,
            type INTEGER NOT NULL,
            name TEXT NOT NULL,
            detail TEXT,
            executable_path TEXT,
            publisher TEXT,
            signature_publisher TEXT,
            enabled INTEGER NOT NULL DEFAULT 1,
            start_mode TEXT,
            state TEXT,
            owner TEXT,
            location TEXT,
            size_bytes INTEGER,
            modified_utc TEXT,
            is_microsoft INTEGER NOT NULL DEFAULT 0,
            is_security_sensitive INTEGER NOT NULL DEFAULT 0,
            supports_pause INTEGER NOT NULL DEFAULT 0,
            pause_json TEXT,
            details_json TEXT,
            status TEXT NOT NULL DEFAULT 'active',
            first_seen TEXT NOT NULL,
            last_seen TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_components_app ON components(app_id);
        CREATE INDEX IF NOT EXISTS idx_components_type ON components(type);

        CREATE TABLE IF NOT EXISTS snapshots (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            captured_at TEXT NOT NULL,
            reason INTEGER NOT NULL,
            app_count INTEGER NOT NULL,
            component_count INTEGER NOT NULL,
            is_complete INTEGER NOT NULL DEFAULT 1,
            sections_json TEXT NOT NULL,
            payload BLOB NOT NULL,
            payload_hash TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS events (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            fingerprint TEXT NOT NULL,
            app_id TEXT,
            app_name TEXT NOT NULL DEFAULT '',
            kind INTEGER NOT NULL,
            component_type INTEGER NOT NULL,
            component_name TEXT NOT NULL DEFAULT '',
            title TEXT NOT NULL,
            description TEXT NOT NULL,
            details_json TEXT NOT NULL,
            confidence INTEGER NOT NULL,
            confidence_reasons_json TEXT NOT NULL,
            importance INTEGER NOT NULL,
            source TEXT NOT NULL DEFAULT '',
            detected_at TEXT NOT NULL,
            snapshot_id INTEGER,
            session_id INTEGER,
            is_reverted INTEGER NOT NULL DEFAULT 0,
            is_read INTEGER NOT NULL DEFAULT 0,
            affected_count INTEGER NOT NULL DEFAULT 1
        );

        CREATE INDEX IF NOT EXISTS idx_events_time ON events(detected_at DESC);
        CREATE INDEX IF NOT EXISTS idx_events_app ON events(app_id);
        CREATE INDEX IF NOT EXISTS idx_events_fingerprint ON events(fingerprint);
        CREATE INDEX IF NOT EXISTS idx_events_importance ON events(importance);

        CREATE TABLE IF NOT EXISTS paused_components (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            component_id TEXT NOT NULL,
            component_type INTEGER NOT NULL,
            app_id TEXT,
            app_name TEXT NOT NULL DEFAULT '',
            display_name TEXT NOT NULL,
            detail TEXT,
            descriptor_blob TEXT NOT NULL,
            paused_at TEXT NOT NULL,
            reason TEXT,
            status INTEGER NOT NULL DEFAULT 0,
            last_error TEXT
        );

        CREATE INDEX IF NOT EXISTS idx_paused_status ON paused_components(status);

        CREATE TABLE IF NOT EXISTS sessions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            started_at TEXT NOT NULL,
            completed_at TEXT,
            baseline_snapshot_id INTEGER,
            installer_path TEXT,
            status INTEGER NOT NULL,
            summary TEXT,
            new_app_count INTEGER NOT NULL DEFAULT 0,
            event_count INTEGER NOT NULL DEFAULT 0
        );

        CREATE INDEX IF NOT EXISTS idx_sessions_status ON sessions(status);
        """;
}
