PRAGMA foreign_keys = OFF;

DROP TABLE IF EXISTS CandidateProfileInfos;
DROP TABLE IF EXISTS UserRoles;
DROP TABLE IF EXISTS ResumeSectionVectors;
DROP TABLE IF EXISTS ResumeSections;
DROP TABLE IF EXISTS ResumeEntries;
DROP TABLE IF EXISTS UserExternalIdentities;
DROP TABLE IF EXISTS UserAccounts;

CREATE TABLE UserAccounts (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Username TEXT NOT NULL,
    Nickname TEXT NOT NULL DEFAULT '',
    Avatar TEXT NOT NULL DEFAULT '🧑‍💻',
    PasswordHash TEXT NOT NULL,
    Role TEXT NOT NULL,
    ProfileHash TEXT NOT NULL DEFAULT '',
    ProfileHashUpdatedAt TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    CandidateProfileVersion INTEGER NOT NULL DEFAULT 1,
    IsProfilePublic INTEGER NOT NULL DEFAULT 1,
    IsDefaultCandidate INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    UpdatedAt TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    IsDeleted INTEGER NOT NULL DEFAULT 0,
    DeletedAt TEXT NULL
);

CREATE TABLE UserExternalIdentities (
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

CREATE TABLE UserRoles (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    UserId INTEGER NOT NULL,
    Role TEXT NOT NULL,
    CreatedAt TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    CONSTRAINT FK_UserRoles_UserAccounts_UserId
        FOREIGN KEY (UserId)
        REFERENCES UserAccounts (Id)
        ON DELETE CASCADE
);

CREATE TABLE CandidateProfileInfos (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
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
    CreatedAt TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    UpdatedAt TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    CreatedByUserId INTEGER NULL,
    CONSTRAINT FK_CandidateProfileInfos_UserAccounts_UserId
        FOREIGN KEY (UserId)
        REFERENCES UserAccounts (Id)
        ON DELETE CASCADE,
    CONSTRAINT FK_CandidateProfileInfos_CandidateProfileInfos_BasedOnInfoId
        FOREIGN KEY (BasedOnInfoId)
        REFERENCES CandidateProfileInfos (Id)
        ON DELETE SET NULL
);

CREATE TABLE ResumeEntries (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    TenantId TEXT NOT NULL,
    Language TEXT NOT NULL DEFAULT 'zh-CN',
    Title TEXT NOT NULL,
    SourceFileName TEXT NULL,
    SourceFileContent BLOB NULL,
    SourceContentType TEXT NULL,
    SourceFileSize INTEGER NULL,
    SourceFileHash TEXT NULL,
    CreatedAt TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    UpdatedAt TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
);

CREATE TABLE ResumeSections (
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

CREATE TABLE ResumeSectionVectors (
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

CREATE UNIQUE INDEX IF NOT EXISTS IX_UserAccounts_ProfileHash
    ON UserAccounts (ProfileHash)
    WHERE ProfileHash <> '';

CREATE UNIQUE INDEX IF NOT EXISTS IX_UserAccounts_DefaultCandidate
    ON UserAccounts (IsDefaultCandidate)
    WHERE IsDefaultCandidate = 1 AND IsDeleted = 0;

CREATE UNIQUE INDEX IF NOT EXISTS IX_UserExternalIdentities_Provider_ProviderUserId
    ON UserExternalIdentities (Provider, ProviderUserId);

CREATE INDEX IF NOT EXISTS IX_UserExternalIdentities_UserId
    ON UserExternalIdentities (UserId);

CREATE UNIQUE INDEX IF NOT EXISTS IX_UserRoles_UserId_Role
    ON UserRoles (UserId, Role);

CREATE INDEX IF NOT EXISTS IX_UserRoles_UserId
    ON UserRoles (UserId);

CREATE UNIQUE INDEX IF NOT EXISTS IX_CandidateProfileInfos_UserId_InfoType_Version
    ON CandidateProfileInfos (UserId, InfoType, Version);

CREATE INDEX IF NOT EXISTS IX_CandidateProfileInfos_UserId_InfoType_IsCurrent
    ON CandidateProfileInfos (UserId, InfoType, IsCurrent);

CREATE INDEX IF NOT EXISTS IX_CandidateProfileInfos_BasedOnInfoId
    ON CandidateProfileInfos (BasedOnInfoId);

CREATE INDEX IF NOT EXISTS IX_ResumeEntries_TenantId_CreatedAt
    ON ResumeEntries (TenantId, CreatedAt);

CREATE UNIQUE INDEX IF NOT EXISTS IX_ResumeEntries_TenantId_Language
    ON ResumeEntries (TenantId, Language);

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

PRAGMA foreign_keys = ON;
