ALTER TABLE devices ADD COLUMN display_name TEXT NOT NULL DEFAULT 'Unbenannte Katze';
INSERT OR IGNORE INTO devices(id,token_hash,public_key,created_at,last_seen,display_name)
VALUES('admin','disabled','MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEf8bZ0ju/s+migXmaMHddYPNeZMM7mzKEtQAdoTbf5KOwm8CE6iN+Lk7cfDk4/VNnrRitat/kdpb2mGaF5/J4hQ==',0,0,'Taskbar Cat Admin');
