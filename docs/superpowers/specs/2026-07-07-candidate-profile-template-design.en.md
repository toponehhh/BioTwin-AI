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

The field can be named `ProfileHash` to match the URL shape, but its value should be generated with a cryptographically strong random token. This prevents guessing profile links from usernames or sequential IDs.

The public URL format is:

```text
/?uid=<current-profile-hash>
```

When `uid` is absent, the page shows the default candidate. When `uid` is present, the page resolves the candidate whose current `ProfileHash` matches the value.

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

## Profile Hash Rotation

`ProfileHash` is a current-version sharing token. It must change whenever the candidate changes any content that appears in the public profile template.

Public profile content includes:

- Display name, nickname, avatar, and public headline.
- Public summary or bio.
- Career timeline items.
- Work/project timeline items.
- Skills, education, certifications, and visible resume sections.
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

Old anonymous links become invalid immediately. Anonymous users should see a generic unavailable or expired-link state. The response should not reveal whether a user exists.

## Public and Authenticated Access

Anonymous public access is intentionally narrow:

```text
GET /api/public/candidate-profile?uid=<profile-hash>
```

Behavior:

- If `uid` is present, resolve by current `UserAccounts.ProfileHash`.
- If `uid` is absent, resolve the default candidate.
- Require `IsProfilePublic = true`.
- Require the user to have the `Candidate` role.
- Require the user to be active and not deleted.
- Return 404 or a generic unavailable response when no profile can be shown.

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

Generate profile hashes in application code using `RandomNumberGenerator`, then encode with base64url or hex. Prefer at least 128 bits of entropy; 192 bits is a comfortable default.

Hash rotation should be centralized in a candidate profile service so future resume-edit endpoints cannot forget to invalidate old public links.

The implementation should avoid storing historical public hashes unless audit requirements appear later. For the first version, only the current `ProfileHash` is valid.
