-- Compatibility script for legacy databases that already have the current
-- UserAccounts profile columns. For a clean development reset, run
-- 001-initial-schema.sql instead; 001 drops data and creates the full schema.
-- This script intentionally does not add UserAccounts columns because SQLite
-- does not support repeatable column-add statements in a portable way.

PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS UserRoles (
    Id INTEGER NOT NULL CONSTRAINT PK_UserRoles PRIMARY KEY AUTOINCREMENT,
    UserId INTEGER NOT NULL,
    Role TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    CONSTRAINT FK_UserRoles_UserAccounts_UserId FOREIGN KEY (UserId) REFERENCES UserAccounts (Id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS IX_UserRoles_UserId_Role
ON UserRoles(UserId, Role);

INSERT OR IGNORE INTO UserRoles (UserId, Role, CreatedAt)
SELECT Id, Role, CreatedAt
FROM UserAccounts
WHERE Role IS NOT NULL AND trim(Role) <> '';

CREATE TABLE IF NOT EXISTS CandidateProfileInfos (
    Id INTEGER NOT NULL CONSTRAINT PK_CandidateProfileInfos PRIMARY KEY AUTOINCREMENT,
    UserId INTEGER NOT NULL,
    InfoType TEXT NOT NULL,
    Version INTEGER NOT NULL,
    JsonData TEXT NOT NULL,
    Source TEXT NOT NULL,
    SourceResumeVersion INTEGER NULL,
    BasedOnInfoId INTEGER NULL,
    ModelName TEXT NULL,
    PromptVersion TEXT NULL,
    IsCurrent INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    CreatedByUserId INTEGER NULL,
    CONSTRAINT FK_CandidateProfileInfos_UserAccounts_UserId FOREIGN KEY (UserId) REFERENCES UserAccounts (Id) ON DELETE CASCADE,
    CONSTRAINT FK_CandidateProfileInfos_CandidateProfileInfos_BasedOnInfoId FOREIGN KEY (BasedOnInfoId) REFERENCES CandidateProfileInfos (Id) ON DELETE SET NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS IX_CandidateProfileInfos_UserId_InfoType_Version
ON CandidateProfileInfos(UserId, InfoType, Version);

CREATE INDEX IF NOT EXISTS IX_CandidateProfileInfos_UserId_InfoType_IsCurrent
ON CandidateProfileInfos(UserId, InfoType, IsCurrent);
