ALTER TABLE devices ADD COLUMN username TEXT COLLATE NOCASE;
UPDATE devices SET username = CASE WHEN id = 'admin' THEN 'taskbar-cat-admin' ELSE 'cat-' || substr(id, 1, 8) END;
CREATE UNIQUE INDEX devices_username_unique ON devices(username COLLATE NOCASE);

CREATE TABLE friendships (
  device_id TEXT NOT NULL,
  friend_id TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  PRIMARY KEY (device_id, friend_id),
  FOREIGN KEY (device_id) REFERENCES devices(id),
  FOREIGN KEY (friend_id) REFERENCES devices(id),
  CHECK (device_id <> friend_id)
);
CREATE INDEX friendships_friend ON friendships(friend_id, device_id);

CREATE TABLE admin_settings (
  id INTEGER PRIMARY KEY CHECK (id = 1),
  sender_name TEXT NOT NULL
);
INSERT INTO admin_settings(id, sender_name) VALUES(1, 'Taskbar Cat Admin');
