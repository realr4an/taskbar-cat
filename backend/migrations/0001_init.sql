CREATE TABLE devices (
  id TEXT PRIMARY KEY,
  token_hash TEXT NOT NULL,
  public_key TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  last_seen INTEGER NOT NULL
);
CREATE TABLE messages (
  id TEXT PRIMARY KEY,
  sender_id TEXT NOT NULL,
  recipient_id TEXT NOT NULL,
  envelope TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  expires_at INTEGER NOT NULL,
  FOREIGN KEY (sender_id) REFERENCES devices(id),
  FOREIGN KEY (recipient_id) REFERENCES devices(id)
);
CREATE INDEX messages_recipient ON messages(recipient_id, created_at);
