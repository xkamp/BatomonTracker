CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    steam_id VARCHAR(32) NOT NULL UNIQUE,
    display_name VARCHAR(120),
    avatar_url TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_login_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS auth_sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    refresh_token_hash TEXT NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    revoked_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS patches (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    version VARCHAR(32) NOT NULL UNIQUE,
    released_at TIMESTAMPTZ,
    is_active BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS seasons (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(120) NOT NULL,
    starts_at TIMESTAMPTZ NOT NULL,
    ends_at TIMESTAMPTZ,
    is_active BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS runs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    external_run_id VARCHAR(160) NOT NULL,
    patch_id UUID REFERENCES patches(id),
    mode VARCHAR(32),
    is_ranked BOOLEAN NOT NULL DEFAULT FALSE,
    trainer_id VARCHAR(120),
    outcome VARCHAR(32),
    started_at TIMESTAMPTZ,
    ended_at TIMESTAMPTZ,
    round INTEGER,
    wins INTEGER,
    lives INTEGER,
    uploaded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (user_id, external_run_id)
);

CREATE TABLE IF NOT EXISTS run_units (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    run_id UUID NOT NULL REFERENCES runs(id) ON DELETE CASCADE,
    location VARCHAR(32) NOT NULL,
    slot INTEGER,
    species_id VARCHAR(120) NOT NULL,
    level INTEGER,
    is_shiny BOOLEAN,
    extra_types JSONB NOT NULL DEFAULT '[]'::jsonb,
    permanent_buffs JSONB NOT NULL DEFAULT '{}'::jsonb
);

CREATE TABLE IF NOT EXISTS run_trinkets (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    run_id UUID NOT NULL REFERENCES runs(id) ON DELETE CASCADE,
    trinket_id VARCHAR(120) NOT NULL,
    acquired_at TIMESTAMPTZ,
    used_at TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS run_events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    run_id UUID NOT NULL REFERENCES runs(id) ON DELETE CASCADE,
    save_seq BIGINT,
    event_type VARCHAR(64) NOT NULL,
    confidence VARCHAR(32) NOT NULL,
    payload JSONB NOT NULL DEFAULT '{}'::jsonb,
    detected_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS patch_notes (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    patch_id UUID REFERENCES patches(id) ON DELETE SET NULL,
    title VARCHAR(200) NOT NULL,
    body TEXT NOT NULL,
    published_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS leaderboard_entries (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    season_id UUID NOT NULL REFERENCES seasons(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    score INTEGER NOT NULL DEFAULT 0,
    wins INTEGER NOT NULL DEFAULT 0,
    games_played INTEGER NOT NULL DEFAULT 0,
    rank INTEGER,
    UNIQUE (season_id, user_id)
);

CREATE TABLE IF NOT EXISTS batomon_catalog (
    species_id VARCHAR(120) PRIMARY KEY,
    display_name VARCHAR(120),
    types JSONB NOT NULL DEFAULT '[]'::jsonb,
    abilities JSONB NOT NULL DEFAULT '{}'::jsonb,
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    patch_id UUID REFERENCES patches(id)
);

CREATE TABLE IF NOT EXISTS trainer_catalog (
    trainer_id VARCHAR(120) PRIMARY KEY,
    display_name VARCHAR(120),
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    patch_id UUID REFERENCES patches(id)
);

CREATE TABLE IF NOT EXISTS trinket_catalog (
    trinket_id VARCHAR(120) PRIMARY KEY,
    display_name VARCHAR(120),
    effect TEXT,
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    patch_id UUID REFERENCES patches(id)
);

CREATE INDEX IF NOT EXISTS ix_runs_user_ended_at ON runs(user_id, ended_at DESC);
CREATE INDEX IF NOT EXISTS ix_run_events_run_id ON run_events(run_id);
CREATE INDEX IF NOT EXISTS ix_leaderboard_season_score ON leaderboard_entries(season_id, score DESC);

CREATE TABLE IF NOT EXISTS analytics_datasets (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    dataset_id VARCHAR(160) NOT NULL UNIQUE,
    file_name VARCHAR(255) NOT NULL,
    mode VARCHAR(32) NOT NULL,
    set_id VARCHAR(64) NOT NULL,
    segment VARCHAR(64),
    etag TEXT,
    fetched_at TIMESTAMPTZ,
    imported_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS analytics_snapshots (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    dataset_id UUID NOT NULL REFERENCES analytics_datasets(id) ON DELETE CASCADE,
    snapshot_hash CHAR(64) NOT NULL,
    user_id VARCHAR(64) NOT NULL,
    display_name VARCHAR(120),
    external_run_id VARCHAR(160) NOT NULL,
    round INTEGER NOT NULL,
    mmr INTEGER,
    rank_tier VARCHAR(64),
    source_set_id VARCHAR(64),
    game_version INTEGER,
    won BOOLEAN NOT NULL,
    wins_before INTEGER,
    lives_lost INTEGER,
    trainer_id VARCHAR(120),
    shop_rank INTEGER,
    raw_json JSONB NOT NULL,
    imported_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (dataset_id, user_id, external_run_id, round, snapshot_hash)
);

CREATE TABLE IF NOT EXISTS analytics_snapshot_units (
    snapshot_id UUID NOT NULL REFERENCES analytics_snapshots(id) ON DELETE CASCADE,
    position INTEGER NOT NULL,
    species_id VARCHAR(120) NOT NULL,
    level INTEGER,
    is_shiny BOOLEAN,
    extra_types JSONB NOT NULL DEFAULT '[]'::jsonb,
    permanent_buffs JSONB,
    blackboard JSONB,
    PRIMARY KEY (snapshot_id, position)
);

CREATE TABLE IF NOT EXISTS analytics_snapshot_trinkets (
    snapshot_id UUID NOT NULL REFERENCES analytics_snapshots(id) ON DELETE CASCADE,
    trinket_id VARCHAR(120) NOT NULL,
    used BOOLEAN NOT NULL DEFAULT FALSE,
    PRIMARY KEY (snapshot_id, trinket_id)
);

CREATE INDEX IF NOT EXISTS ix_analytics_snapshots_run
    ON analytics_snapshots(dataset_id, user_id, external_run_id, round);
CREATE INDEX IF NOT EXISTS ix_analytics_snapshots_round
    ON analytics_snapshots(dataset_id, round, mmr);
CREATE INDEX IF NOT EXISTS ix_analytics_snapshot_units_species
    ON analytics_snapshot_units(species_id);

CREATE OR REPLACE VIEW analytics_snapshot_facts AS
SELECT
    snapshot.id,
    snapshot.dataset_id,
    snapshot.user_id,
    snapshot.external_run_id,
    snapshot.round,
    snapshot.mmr,
    snapshot.rank_tier,
    snapshot.won,
    snapshot.wins_before,
    snapshot.lives_lost,
    CASE
        WHEN snapshot.wins_before >= 10 THEN 'extended'
        ELSE 'standard'
    END AS phase,
    snapshot.wins_before + CASE WHEN snapshot.won THEN 1 ELSE 0 END AS wins_after
FROM analytics_snapshots snapshot;

CREATE OR REPLACE VIEW analytics_run_facts AS
SELECT
    snapshot.dataset_id,
    snapshot.user_id,
    snapshot.external_run_id,
    MIN(snapshot.round) AS first_round,
    MAX(snapshot.round) AS last_round,
    COUNT(*) AS snapshot_count,
    MAX(snapshot.wins_before + CASE WHEN snapshot.won THEN 1 ELSE 0 END) AS max_wins_observed,
    MAX(snapshot.lives_lost) AS max_lives_lost,
    BOOL_OR(snapshot.wins_before >= 10 OR (snapshot.wins_before >= 9 AND snapshot.won)) AS reached_ten_wins,
    BOOL_OR(snapshot.lives_lost >= 10) AS reached_zero_lives,
    BOOL_OR(snapshot.wins_before >= 10) AS has_extended_phase,
    BOOL_OR(snapshot.wins_before >= 10 OR (snapshot.wins_before >= 9 AND snapshot.won) OR snapshot.lives_lost >= 10) AS is_complete
FROM analytics_snapshots snapshot
GROUP BY snapshot.dataset_id, snapshot.user_id, snapshot.external_run_id;
