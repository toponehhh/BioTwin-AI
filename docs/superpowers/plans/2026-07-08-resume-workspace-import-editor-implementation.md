# Resume Workspace Import Editor Implementation Plan

> This plan follows the Superpowers writing-plans workflow. During execution, use test-driven-development for behavior changes and verification-before-completion before reporting completion.

## Scope

Implement the approved Resume Workspace design:

- Add language-aware resume contracts for `zh-CN` and `en`.
- Enforce one canonical resume per tenant/language in backend save behavior.
- Add a non-persistent merge preview API for same-language imports.
- Update SQLite schema scripts and startup validation for `ResumeEntries.Language`.
- Build the Blazor Resume Workspace UI with a library pane, Markdown editor wrapper, live outline tree, and merge preview state.

## Constraints

- Do not add startup-time schema or seed mutations.
- Keep manual SQL scripts under `src/BioTwin_AI.AspNetCoreApi/Infrastructure/Data/Migrations`.
- Do not introduce a frontend build pipeline.
- Preserve existing upload/edit routes while adding `/resume/workspace`.
- Do not clean or revert unrelated working-tree changes.

## Tasks

### 1. Shared Contracts

- Add shared language constants for `zh-CN` and `en`.
- Add `Language` to `ResumeSummaryDto`, `ResumeDetailDto`, and `SaveResumeMarkdownRequest`.
- Add `DetectedLanguage` to `ConvertedResumeFileDto`.
- Add `MergeResumeMarkdownRequest` and `MergeResumeMarkdownResponse`.
- Update shared contract tests to verify serialization and expected properties.

### 2. Data Model And Schema

- Add `ResumeEntry.Language` with default `zh-CN`.
- Configure EF model with required max length and unique index on `(TenantId, Language)`.
- Update `001-initial-schema.sql` with `Language` and `IX_ResumeEntries_TenantId_Language`.
- Add a manual migration script for existing DBs.
- Update `DatabaseSchemaValidator` to require `ResumeEntries.Language` and the language unique index.

### 3. Backend Resume Behavior

- Validate all incoming resume languages.
- Detect converted upload language deterministically from Markdown.
- Change `SaveAsync` so `(tenantId, language)` creates when absent and updates when present.
- Keep duplicate source-hash behavior, but do not let it bypass language information in returned DTOs.
- Add `MergePreviewAsync` to `IResumeService`.
- Add `POST /api/resumes/merge-preview`.
- Implement merge preview without writing database rows; use a deterministic local merge fallback for the first version, structured so an LLM-backed merge service can replace it.
- Keep save/replace section splitting, vector generation, and candidate profile refresh.

### 4. Blazor Resume Workspace UI

- Add `Pages/ResumeWorkspace.razor` at `/resume/workspace`.
- Add a small Markdown editor component and `wwwroot/js/markdownEditor.js` bridge.
- Include EasyMDE from CDN in `index.html`; bridge falls back to textarea behavior if the vendor script is unavailable.
- Build the three-pane glass workspace:
  - Left library with language slots and imported drafts.
  - Center editor with title, language selector, upload, save, and merge preview actions.
  - Right outline tree parsed from live Markdown headings.
- Keep `/resume/upload` and `/resume/edit/{ResumeId:int?}` intact for compatibility.

### 5. Tests And Verification

- Add/update API tests:
  - first language save creates one resume;
  - same-language save updates the canonical resume;
  - `zh-CN` and `en` can coexist;
  - merge preview does not write `ResumeEntries`.
- Add/update Blazor route scaffolding tests for workspace, editor bridge, language selector, library, and outline.
- Run targeted test projects with the user-provided .NET SDK path.
- Run a build if tests pass or after fixing compile issues.

