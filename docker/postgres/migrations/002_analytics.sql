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
