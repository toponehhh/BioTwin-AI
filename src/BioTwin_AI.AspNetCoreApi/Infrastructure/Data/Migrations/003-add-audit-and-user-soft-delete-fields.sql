PRAGMA foreign_keys = ON;

UPDATE UserAccounts
SET UpdatedAt = CreatedAt
WHERE UpdatedAt = '1970-01-01T00:00:00.000Z';

UPDATE UserExternalIdentities
SET CreatedAt = LinkedAt
WHERE CreatedAt = '1970-01-01T00:00:00.000Z';

UPDATE UserExternalIdentities
SET UpdatedAt = CreatedAt
WHERE UpdatedAt = '1970-01-01T00:00:00.000Z';

UPDATE ResumeEntries
SET UpdatedAt = CreatedAt
WHERE UpdatedAt = '1970-01-01T00:00:00.000Z';

UPDATE ResumeSections
SET UpdatedAt = CreatedAt
WHERE UpdatedAt = '1970-01-01T00:00:00.000Z';

UPDATE ResumeSectionVectors
SET UpdatedAt = CreatedAt
WHERE UpdatedAt = '1970-01-01T00:00:00.000Z';
