CREATE TABLE rate_events (
  event_key TEXT NOT NULL,
  created_at INTEGER NOT NULL
);
CREATE INDEX rate_events_key_time ON rate_events(event_key, created_at);
