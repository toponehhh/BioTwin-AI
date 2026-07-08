PRAGMA foreign_keys = OFF;

DROP TABLE IF EXISTS ResumeEntries_new;

CREATE TABLE ResumeEntries_new (
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

INSERT INTO ResumeEntries_new (
    Id,
    TenantId,
    Language,
    Title,
    SourceFileName,
    SourceFileContent,
    SourceContentType,
    SourceFileSize,
    SourceFileHash,
    CreatedAt,
    UpdatedAt
)
SELECT
    Id,
    TenantId,
    'zh-CN',
    Title,
    SourceFileName,
    SourceFileContent,
    SourceContentType,
    SourceFileSize,
    SourceFileHash,
    CreatedAt,
    UpdatedAt
FROM ResumeEntries
WHERE Id IN (
    SELECT Id
    FROM (
        SELECT
            Id,
            ROW_NUMBER() OVER (
                PARTITION BY TenantId
                ORDER BY UpdatedAt DESC, CreatedAt DESC, Id DESC
            ) AS RowNumber
        FROM ResumeEntries
    )
    WHERE RowNumber = 1
);

DROP TABLE ResumeEntries;

ALTER TABLE ResumeEntries_new RENAME TO ResumeEntries;

CREATE INDEX IF NOT EXISTS IX_ResumeEntries_TenantId_CreatedAt
    ON ResumeEntries (TenantId, CreatedAt);

CREATE UNIQUE INDEX IF NOT EXISTS IX_ResumeEntries_TenantId_Language
    ON ResumeEntries (TenantId, Language);

CREATE UNIQUE INDEX IF NOT EXISTS IX_ResumeEntries_TenantId_SourceFileHash
    ON ResumeEntries (TenantId, SourceFileHash);

DELETE FROM ResumeSectionVectors
WHERE ResumeSectionId IN (
    SELECT ResumeSections.Id
    FROM ResumeSections
    WHERE ResumeSections.ResumeEntryId NOT IN (
        SELECT Id
        FROM ResumeEntries
    )
);

DELETE FROM ResumeSections
WHERE ResumeEntryId NOT IN (
    SELECT Id
    FROM ResumeEntries
);

PRAGMA foreign_keys = ON;
