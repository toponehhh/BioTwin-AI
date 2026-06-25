PRAGMA foreign_keys = ON;

BEGIN TRANSACTION;

CREATE TABLE IF NOT EXISTS UserAccounts (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Username TEXT NOT NULL,
    Nickname TEXT NOT NULL DEFAULT '',
    Avatar TEXT NOT NULL DEFAULT '🧑‍💻',
    PasswordHash TEXT NOT NULL,
    Role TEXT NOT NULL,
    CreatedAt TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    UpdatedAt TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    IsDeleted INTEGER NOT NULL DEFAULT 0,
    DeletedAt TEXT NULL
);

CREATE TABLE IF NOT EXISTS UserExternalIdentities (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    UserId INTEGER NOT NULL,
    Provider TEXT NOT NULL,
    ProviderUserId TEXT NOT NULL,
    ProviderEmail TEXT NULL,
    ProviderEmailVerified INTEGER NOT NULL DEFAULT 0,
    ProviderDisplayName TEXT NULL,
    ProviderAvatarUrl TEXT NULL,
    CreatedAt TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    UpdatedAt TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    LinkedAt TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    LastLoginAt TEXT NULL,
    RawClaimsJson TEXT NULL,
    CONSTRAINT FK_UserExternalIdentities_UserAccounts_UserId
        FOREIGN KEY (UserId)
        REFERENCES UserAccounts (Id)
        ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS ResumeEntries (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    TenantId TEXT NOT NULL,
    Title TEXT NOT NULL,
    SourceFileName TEXT NULL,
    SourceFileContent BLOB NULL,
    SourceContentType TEXT NULL,
    SourceFileSize INTEGER NULL,
    SourceFileHash TEXT NULL,
    CreatedAt TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    UpdatedAt TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
);

CREATE TABLE IF NOT EXISTS ResumeSections (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    ResumeEntryId INTEGER NOT NULL,
    ParentSectionId INTEGER NULL,
    TenantId TEXT NOT NULL,
    HeadingLevel INTEGER NOT NULL DEFAULT 2,
    Title TEXT NOT NULL,
    Content TEXT NOT NULL,
    SortOrder INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    UpdatedAt TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    CONSTRAINT FK_ResumeSections_ResumeEntries_ResumeEntryId
        FOREIGN KEY (ResumeEntryId)
        REFERENCES ResumeEntries (Id)
        ON DELETE CASCADE,
    CONSTRAINT FK_ResumeSections_ResumeSections_ParentSectionId
        FOREIGN KEY (ParentSectionId)
        REFERENCES ResumeSections (Id)
        ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS ResumeSectionVectors (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    ResumeSectionId INTEGER NOT NULL,
    TenantId TEXT NOT NULL,
    ResumeTitle TEXT NOT NULL,
    SectionTitle TEXT NOT NULL,
    Content TEXT NOT NULL,
    EmbeddingPayload TEXT NOT NULL,
    CreatedAt TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    UpdatedAt TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    CONSTRAINT FK_ResumeSectionVectors_ResumeSections_ResumeSectionId
        FOREIGN KEY (ResumeSectionId)
        REFERENCES ResumeSections (Id)
        ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS IX_UserAccounts_Username
    ON UserAccounts (Username);

CREATE UNIQUE INDEX IF NOT EXISTS IX_UserExternalIdentities_Provider_ProviderUserId
    ON UserExternalIdentities (Provider, ProviderUserId);

CREATE INDEX IF NOT EXISTS IX_UserExternalIdentities_UserId
    ON UserExternalIdentities (UserId);

CREATE INDEX IF NOT EXISTS IX_ResumeEntries_TenantId_CreatedAt
    ON ResumeEntries (TenantId, CreatedAt);

CREATE UNIQUE INDEX IF NOT EXISTS IX_ResumeEntries_TenantId_SourceFileHash
    ON ResumeEntries (TenantId, SourceFileHash);

CREATE INDEX IF NOT EXISTS IX_ResumeSections_ResumeEntryId_SortOrder
    ON ResumeSections (ResumeEntryId, SortOrder);

CREATE INDEX IF NOT EXISTS IX_ResumeSections_ParentSectionId
    ON ResumeSections (ParentSectionId);

CREATE UNIQUE INDEX IF NOT EXISTS IX_ResumeSectionVectors_ResumeSectionId
    ON ResumeSectionVectors (ResumeSectionId);

CREATE INDEX IF NOT EXISTS IX_ResumeSectionVectors_TenantId
    ON ResumeSectionVectors (TenantId);

COMMIT;
