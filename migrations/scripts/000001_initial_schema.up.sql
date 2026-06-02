CREATE TYPE incident_status AS ENUM('open', 'resolved');

CREATE TABLE business_incident
(
    id              uuid PRIMARY KEY         DEFAULT gen_random_uuid(),
    previous_id     uuid REFERENCES business_incident (id),
    source          text            NOT NULL,
    ce_id           text            NOT NULL,
    subject         text            NOT NULL,
    description     text            NOT NULL,
    status          incident_status NOT NULL DEFAULT 'open',
    batch_id        text,
    context         jsonb,
    retry_count     int             NOT NULL DEFAULT 0,
    triggered_at    timestamptz     NOT NULL,
    last_retried_at timestamptz,
    resolved_at     timestamptz,
    resolved_by     text CHECK (resolved_by IN ('auto', 'manual', 'bulk'))
);

CREATE UNIQUE INDEX uq_incident_open
    ON business_incident (source, ce_id)
    WHERE status = 'open';

CREATE INDEX ix_incident_source ON business_incident (source);
CREATE INDEX ix_incident_status ON business_incident (status);
CREATE INDEX ix_incident_triggered ON business_incident (triggered_at DESC);

CREATE TABLE incident_event
(
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    incident_id uuid        NOT NULL REFERENCES business_incident (id),
    event_type  text        NOT NULL CHECK (event_type IN (
                                                           'triggered', 'retried', 'resolved_auto', 'resolved_manual',
                                                           'recurred'
        )),
    metadata    jsonb,
    occurred_at timestamptz NOT NULL
);

CREATE INDEX ix_incident_event_incident
    ON incident_event (incident_id, occurred_at DESC);

CREATE TABLE outbox_event
(
    id            uuid PRIMARY KEY     DEFAULT gen_random_uuid(),
    incident_id   uuid        NOT NULL REFERENCES business_incident (id),
    event_type    text        NOT NULL,
    created_at    timestamptz NOT NULL DEFAULT now(),
    claimed_at    timestamptz,
    attempt_count int         NOT NULL DEFAULT 0,
    published_at  timestamptz
);

CREATE INDEX ix_outbox_pending
    ON outbox_event (created_at)
    WHERE published_at IS NULL AND attempt_count < 10;
