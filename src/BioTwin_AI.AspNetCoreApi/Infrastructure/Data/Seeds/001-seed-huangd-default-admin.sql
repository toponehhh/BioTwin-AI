-- Seeds a local test user for an empty BioTwin API database.
-- Username: huangd
-- Password: !QazxsW@
--
-- Run this after the schema scripts have been applied.
-- This script is intentionally idempotent and does not start its own transaction.

PRAGMA foreign_keys = ON;

UPDATE UserAccounts
SET IsDefaultCandidate = 0,
    UpdatedAt = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
WHERE Username <> 'huangd'
  AND IsDefaultCandidate = 1
  AND IsDeleted = 0;

INSERT OR IGNORE INTO UserAccounts (
    Username,
    Nickname,
    Avatar,
    PasswordHash,
    Role,
    ProfileHash,
    ProfileHashUpdatedAt,
    CandidateProfileVersion,
    IsProfilePublic,
    IsDefaultCandidate,
    CreatedAt,
    UpdatedAt,
    IsDeleted,
    DeletedAt
)
VALUES (
    'huangd',
    'Donald Huang',
    'HD',
    'qA9F1nzPQw6aWtVXoQ7dEw==:9jLA3bKb+byrd2e1k1uaQbryV3DjsATTeHBIbXAwWGI=',
    'Admin',
    'HUANGD01',
    strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
    1,
    1,
    1,
    strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
    strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
    0,
    NULL
);

UPDATE UserAccounts
SET Nickname = 'Donald Huang',
    Avatar = 'HD',
    PasswordHash = 'qA9F1nzPQw6aWtVXoQ7dEw==:9jLA3bKb+byrd2e1k1uaQbryV3DjsATTeHBIbXAwWGI=',
    Role = 'Admin',
    ProfileHash = CASE
        WHEN ProfileHash IS NULL OR trim(ProfileHash) = '' THEN 'HUANGD01'
        ELSE ProfileHash
    END,
    ProfileHashUpdatedAt = CASE
        WHEN ProfileHash IS NULL OR trim(ProfileHash) = '' THEN strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
        ELSE ProfileHashUpdatedAt
    END,
    CandidateProfileVersion = CASE
        WHEN CandidateProfileVersion < 1 THEN 1
        ELSE CandidateProfileVersion
    END,
    IsProfilePublic = 1,
    IsDefaultCandidate = 1,
    UpdatedAt = strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
    IsDeleted = 0,
    DeletedAt = NULL
WHERE Username = 'huangd';

INSERT OR IGNORE INTO UserRoles (UserId, Role, CreatedAt)
SELECT Id, 'Candidate', strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
FROM UserAccounts
WHERE Username = 'huangd';

INSERT OR IGNORE INTO UserRoles (UserId, Role, CreatedAt)
SELECT Id, 'Admin', strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
FROM UserAccounts
WHERE Username = 'huangd';
