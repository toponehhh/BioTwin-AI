PRAGMA foreign_keys = ON;

UPDATE UserAccounts
SET Nickname = Username
WHERE Nickname IS NULL OR trim(Nickname) = '';

UPDATE UserAccounts
SET Avatar = '🧑‍💻'
WHERE Avatar IS NULL OR trim(Avatar) = '';
