PRAGMA foreign_keys = ON;

BEGIN TRANSACTION;

ALTER TABLE UserAccounts ADD COLUMN Nickname TEXT NOT NULL DEFAULT '';
ALTER TABLE UserAccounts ADD COLUMN Avatar TEXT NOT NULL DEFAULT '🧑‍💻';

UPDATE UserAccounts
SET Nickname = Username
WHERE Nickname IS NULL OR trim(Nickname) = '';

UPDATE UserAccounts
SET Avatar = '🧑‍💻'
WHERE Avatar IS NULL OR trim(Avatar) = '';

COMMIT;
