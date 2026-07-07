# Candidate Profile Template, Sharing, Roles, and Timeline Design

Date: 2026-07-07

## Purpose

BioTwin_AI needs one reusable candidate profile template that can render any candidate's public-facing information. Anonymous visitors can view a single candidate only through a shared URL. Interviewers who need to browse multiple candidates must log in and use authenticated pages.

The same template must support:

- The default candidate when no query string is provided.
- A specific candidate when the URL contains `uid=<profile-hash>`.
- The vertical career timeline inspired by `My career & experience`.
- The horizontal work timeline inspired by `My Work`.

## Key Decisions

Use a profile hash as an opaque public sharing token, not as a deterministic hash of username, user ID, or resume content.

The field can be named `ProfileHash` to match the URL shape, but its value should be generated as a short, cryptographically random share code. It must not be derived from username, user ID, or resume content. This prevents guessing profile links from usernames or sequential IDs while keeping the link easy to share.

The share code should be short enough to type manually. Use an unambiguous uppercase alphabet such as Crockford Base32 and exclude confusing characters like `0`, `O`, `1`, `I`, and `L`. The production default should be 8 characters. Six characters can be supported as a configurable lower bound, but 8 characters is the recommended default for anonymous public access.

The public URL format is:

```text
/?uid=<current-profile-code>
```

Example:

```text
/?uid=K7X4Q9MF
```

When `uid` is absent, the page shows the default candidate. When `uid` is present, the page normalizes the value to the canonical uppercase form and resolves the candidate whose current `ProfileHash` matches it.

## Database Shape

Extend `UserAccounts` with candidate profile sharing metadata:

```text
ProfileHash text unique not null
ProfileHashUpdatedAt datetime not null
CandidateProfileVersion integer not null default 1
IsProfilePublic integer not null default 1
IsDefaultCandidate integer not null default 0
```

Add a unique partial index so only one non-deleted user can be the default candidate:

```text
unique where IsDefaultCandidate = 1 and IsDeleted = 0
```

Move roles to a many-to-many table:

```text
UserRoles
- Id integer primary key
- UserId integer not null
- Role text not null
- CreatedAt datetime not null
- unique(UserId, Role)
```

Keep the existing `UserRole` enum values as role names:

```text
Candidate
Interviewer
Admin
```

Existing single-role data should be migrated into `UserRoles`. The old `UserAccounts.Role` column can remain temporarily during migration, but new authorization checks should read from `UserRoles`.

## Generated Profile Information Store

Timeline and other display data should be generated from the candidate's resume by calling the local large language model. The model extracts structured JSON that can be applied directly to the public candidate profile template.

Users must be able to manually correct the generated data. Generated data and manual corrections should be stored as versions rather than overwriting the previous value. The latest version for each information type becomes the current source for rendering.

All semi-structured display data can live in one table:

```text
CandidateProfileInfos
- Id integer primary key
- UserId integer not null
- InfoType text not null
- Version integer not null
- JsonData text not null
- Source text not null
- SourceResumeVersion integer null
- BasedOnInfoId integer null
- ModelName text null
- PromptVersion text null
- IsCurrent integer not null default 0
- CreatedAt datetime not null
- UpdatedAt datetime not null
- CreatedByUserId integer null
- unique(UserId, InfoType, Version)
```

`InfoType` identifies which template data the record contains. Initial values should include:

```text
careertimeline
worktimeline
```

The table should allow future values such as:

```text
skillmatrix
educationtimeline
projectgallery
profilesummary
```

`Source` identifies how the version was created:

```text
llm_generated
manual_edit
admin_edit
imported
```

When a candidate changes resume content, the system should load the latest current `CandidateProfileInfos` version for each affected `InfoType` and pass it to the model as reference context. The model then generates the next version instead of starting from scratch. This helps preserve user corrections and gives the model continuity between resume revisions.

Manual correction should create a new version with `Source = manual_edit`, `BasedOnInfoId` pointing to the generated or previous version, and `IsCurrent = true`. Older versions remain stored for traceability and future model reference.

## Profile Hash Rotation

`ProfileHash` is a current-version sharing token. It must change whenever the candidate changes any content that appears in the public profile template.

Public profile content includes:

- Display name, nickname, avatar, and public headline.
- Public summary or bio.
- Current `CandidateProfileInfos` records such as `careertimeline`, `worktimeline`, and future `skillmatrix` data.
- Skills, education, certifications, and visible resume sections when they are rendered directly or represented by current `CandidateProfileInfos` records.
- Public contact fields if the template includes them.

Changes that should not rotate the hash:

- Password changes.
- Login metadata.
- Role changes.
- Default-candidate changes.
- Admin-only notes.
- Internal status fields that are not visible in the public template.

Rotation should happen in the same transaction as the resume/profile save:

```text
CandidateProfileVersion += 1
ProfileHash = GenerateRandomProfileHash()
ProfileHashUpdatedAt = now
```

When resume changes trigger LLM extraction, the public link should also be invalidated before the newly generated display data is shared. Once the user reviews or accepts the generated JSON, the current `CandidateProfileInfos` versions become the source for the new shared profile.

Old anonymous links become invalid immediately. Anonymous users should see a generic unavailable or expired-link state. The response should not reveal whether a user exists.

## Public and Authenticated Access

Anonymous public access is intentionally narrow:

```text
GET /api/public/candidate-profile?uid=<profile-hash>
```

Behavior:

- If `uid` is present, resolve by current `UserAccounts.ProfileHash`.
- Normalize `uid` to the canonical uppercase share-code format before lookup.
- If `uid` is absent, resolve the default candidate.
- Require `IsProfilePublic = true`.
- Require the user to have the `Candidate` role.
- Require the user to be active and not deleted.
- Return 404 or a generic unavailable response when no profile can be shown.
- Rate-limit anonymous lookup attempts to reduce guessing risk for short share codes.

Authenticated interviewer access is separate:

```text
GET /api/interviewer/candidates
GET /api/interviewer/candidates/{id}
```

Behavior:

- Requires login.
- Requires `Interviewer` or `Admin` role.
- Allows browsing, searching, and switching between multiple candidates.
- Does not depend on public `ProfileHash`.
- Does not invalidate when a candidate rotates their public sharing link.

## Default Candidate Resolution

When no `uid` is supplied, resolve the default candidate in this order:

1. A non-deleted user with `IsDefaultCandidate = true`, `IsProfilePublic = true`, and the `Candidate` role.
2. If no explicit default exists, a non-deleted admin user that also has the `Candidate` role.
3. If neither exists, return a generic empty or unavailable public profile state.

This keeps the public home page stable while still letting administrators choose which candidate represents the default site experience.

## Candidate Profile Template DTO

The frontend should render one shared template for both default and shared-link profiles.

The backend should build this DTO from the latest current `CandidateProfileInfos` record for each `InfoType`, plus the candidate account/profile metadata.

```text
CandidateProfileDto
- Candidate
  - DisplayName
  - Headline
  - Avatar
  - Summary
  - ProfileHashUpdatedAt
  - CandidateProfileVersion
- CareerTimelineItems
  - Title
  - Subtitle
  - PeriodLabel
  - SortOrder
  - Description
  - IsHighlighted
- WorkTimelineItems
  - Number
  - Title
  - Description
  - ImageUrl
  - LinkUrl
  - SortOrder
- Skills
- Education
- Certifications
- PublicContact
```

The API should not expose username, password fields, deleted metadata, or internal admin notes.

## Timeline UI Direction

The template includes two timeline sections.

The career timeline is vertical:

- Dark background with purple accent lighting.
- Large centered heading.
- Left column for role title and category.
- Middle column for year or period label.
- Center glowing vertical line with active indicator.
- Right column for description.
- Later or less prominent items can fade subtly.

The work timeline is horizontal:

- Dark grid or rail layout.
- Numbered work items such as `01`, `02`, `03`.
- Project preview media plus title and summary.
- Horizontal scrolling on small screens.
- Same data contract for default and shared candidate profiles.

## Admin and Candidate Management

Admin user management should include:

- Assigning multiple roles to a user.
- Setting exactly one default candidate.
- Toggling whether a candidate profile is public.
- Copying the candidate's current public share URL.
- Manually regenerating `ProfileHash` if needed.

Candidate resume editing should automatically regenerate `ProfileHash` after every successful public-profile content save. The UI should show the new share URL after saving so the candidate knows the previous link expired.

## Testing Strategy

Backend tests:

- Migrates existing `UserAccounts.Role` values into `UserRoles`.
- Enforces one default candidate.
- Generates short unique `ProfileHash` values with the configured length.
- Normalizes `uid` casing before lookup.
- Handles `ProfileHash` collisions by retrying generation.
- Stores LLM-extracted display JSON in `CandidateProfileInfos`.
- Creates a new `CandidateProfileInfos` version for manual corrections.
- Passes the latest current version to the LLM as reference when regenerating after resume changes.
- Resolves default candidate without `uid`.
- Resolves current candidate by `ProfileHash`.
- Rejects old `ProfileHash` after resume changes.
- Rejects deleted, private, or non-candidate users.
- Allows logged-in interviewers to list multiple candidates without `ProfileHash`.

Frontend tests:

- Public page calls the profile API without `uid` for the default candidate.
- Public page passes `uid` from query string when present.
- Shared template renders the same sections for default and selected candidates.
- Anonymous users do not see interviewer candidate navigation.
- Logged-in interviewers can access multi-candidate views.

Visual verification:

- Capture desktop and mobile screenshots for the public candidate template.
- Verify the vertical career timeline and horizontal work timeline do not overlap or clip text.
- Verify the floating top menu remains usable on profile pages.

## Open Implementation Notes

Generate profile hashes in application code using `RandomNumberGenerator`, then encode with an unambiguous uppercase alphabet. The default production code length should be 8 characters. Check the database unique index before publishing a new code and retry generation on collision.

Because the share code is intentionally short, the public lookup endpoint should include rate limiting and generic error responses. The code is a public locator, not an authentication credential.

Hash rotation should be centralized in a candidate profile service so future resume-edit endpoints cannot forget to invalidate old public links.

The implementation should avoid storing historical public hashes unless audit requirements appear later. For the first version, only the current `ProfileHash` is valid.

Profile display JSON schemas should be validated per `InfoType` before a version is marked current. Invalid model output should be stored only as a failed generation artifact or rejected before publication.
