# Resume Workspace Import and Markdown Editor Design

## Summary

Build a unified Resume Workspace for signed-in users. The workspace replaces the split upload/edit flow with one place to import files, review or edit Markdown, inspect the Markdown outline, and save the canonical resume for a language.

The system supports two resume languages for now:

- `zh-CN`: Simplified Chinese
- `en`: English

Each user can have at most one resume per supported language. Importing another resume in the same language does not create a second resume. Instead, the system prepares an AI-assisted merge preview, lets the user review the merged Markdown, and only updates the canonical resume after the user saves.

## Goals

- Allow users to select and import different resume files from one workspace.
- Convert imported files to Markdown using the existing upload conversion pipeline.
- Replace plain textarea editing with a real Markdown editor.
- Show a live tree outline of the current Markdown heading structure.
- Let users choose or correct the detected resume language before saving or merging.
- Enforce one canonical resume per user per language.
- When saving a resume, parse Markdown into sections, regenerate embeddings, and refresh candidate profile-derived data.
- When importing a same-language resume, merge by timeline with an LLM and require user review before saving.

## Non-Goals

- Do not add more languages yet.
- Do not build a full block-based resume editor.
- Do not silently overwrite an existing same-language resume.
- Do not introduce a new frontend build pipeline unless the chosen editor absolutely requires it.
- Do not let startup code mutate database schema or seed data.

## User Experience

### Layout

The workspace is a dense admin tool, not a landing page.

- Left pane: `Library`
  - Shows the two language slots: Simplified Chinese and English.
  - Shows existing canonical resumes when present.
  - Shows imported drafts that have not been saved yet.
  - Includes the file import control.
- Center pane: Markdown editor
  - Edits the selected draft or canonical resume Markdown.
  - Includes title and language controls.
  - Shows primary actions based on state.
- Right pane: Outline
  - Displays a live nested tree parsed from Markdown headings.
  - Uses heading levels to show hierarchy.
  - After save, can be refreshed from backend `ResumeSectionDto.Children`.

### Import Flow

1. User selects one or more files.
2. For each file, the frontend calls the existing upload conversion API.
3. Converted Markdown becomes a draft in the left pane.
4. The workspace detects language from Markdown content.
5. The user can manually change the language before saving or merging.
6. If no canonical resume exists for that language, the main action is `Save`.
7. If a canonical resume already exists for that language, the main action is `Merge preview`.

### New-Language Save Flow

1. User reviews or edits the draft Markdown.
2. User clicks `Save`.
3. Backend creates a `ResumeEntry` with the selected language.
4. Backend splits Markdown into `ResumeSections`.
5. Backend regenerates embeddings in `ResumeSectionVectors`.
6. Backend triggers candidate profile extraction using the final Markdown.
7. Workspace refreshes the library and outline.

### Same-Language Merge Flow

1. User imports a resume whose language already has a canonical resume.
2. User clicks `Merge preview`.
3. Backend loads the canonical resume Markdown and combines it with the imported draft Markdown.
4. LLM merges the two Markdown documents, prioritizing chronological timeline consistency.
5. Backend returns merged Markdown without saving.
6. Workspace loads the merged Markdown into the editor as a reviewable draft.
7. User edits if needed and clicks `Save`.
8. Backend updates the canonical resume for that language, re-splits sections, regenerates vectors, and refreshes candidate profile data.

## Language Detection

Language detection should be lightweight and deterministic on the client or shared code:

- If CJK characters dominate meaningful text, choose `zh-CN`.
- If Latin letters dominate meaningful text, choose `en`.
- If unclear, default to `zh-CN` for now and let the user change it.

The language selector is always visible for drafts and editable resumes.

## Data Model

Add `Language` to `ResumeEntries`.

Recommended values:

- `zh-CN`
- `en`

Add a unique index:

```sql
CREATE UNIQUE INDEX IF NOT EXISTS IX_ResumeEntries_TenantId_Language
ON ResumeEntries(TenantId, Language);
```

Existing data migration behavior:

- Existing resumes need a language assigned manually or through a one-time SQL script.
- If existing databases are empty during development, the initial schema can include the final field and index.
- Runtime startup must only validate schema, not alter data.

## Shared Contracts

Add language to resume-related DTOs and requests:

- `ResumeSummaryDto.Language`
- `ResumeDetailDto.Language`
- `ConvertedResumeFileDto.DetectedLanguage`
- `SaveResumeMarkdownRequest.Language`
- New `MergeResumeMarkdownRequest`
- New `MergeResumeMarkdownResponse`

The request for merge preview should include:

- language
- draft title
- draft Markdown
- source file metadata if needed for UI context

The response should include:

- language
- canonical resume id
- merged title
- merged Markdown
- optional warnings

## Backend Services

### ResumeService

Extend existing behavior:

- Save creates the canonical resume when no resume exists for `(tenantId, language)`.
- Save updates the canonical resume when one exists for `(tenantId, language)`.
- Replace Markdown continues to split sections, regenerate vectors, and trigger candidate profile extraction.
- Duplicate detection by source hash remains useful, but language uniqueness is a separate rule.

### ResumeMergeService

Add a focused service for merge preview:

- Inputs: canonical Markdown, draft Markdown, language, title context.
- Output: merged Markdown.
- Prompt should tell the model to preserve factual details, merge by chronological order, remove duplicate events, and keep Markdown headings.
- If LLM merge fails, return a clear error and keep both documents available to the user.

The first implementation can use the existing configured chat/model infrastructure if available. It should not introduce a remote-only dependency if the project is configured for local model workflows later.

## Frontend Components

### ResumeWorkspace.razor

Routes:

- Preferred route: `/resume/workspace`
- Existing `/resume/upload` and `/resume/edit/{ResumeId:int?}` can redirect to or embed the workspace later.

State model:

- canonical resume summaries by language
- draft items from imported files
- selected item
- current title
- current language
- current Markdown
- dirty flag
- merge preview status
- status/error message

### Markdown Editor

Use a static vendor integration first.

Preferred candidate: EasyMDE.

Reasons:

- Purpose-built Markdown editor.
- Lower integration cost than CodeMirror 6.
- No npm or Vite pipeline required for the first version.

Integration should be wrapped behind:

- `wwwroot/js/markdownEditor.js`
- a small Blazor component such as `MarkdownEditor.razor`

The component should support:

- setting initial Markdown
- change notification
- read/write value from Blazor
- disposal

### Outline Tree

The outline tree can be built client-side from Markdown.

Parsing rules:

- Parse lines matching ATX headings: `#`, `##`, `###`, etc.
- Ignore headings inside fenced code blocks.
- Preserve heading level and text.
- Nest by heading level.
- If no headings exist, show an empty state.

Later, the saved backend section tree can be used to validate or replace the live outline after save.

## API

Existing APIs to reuse:

- `GET /api/resumes`
- `GET /api/resumes/{resumeId}`
- `POST /api/resumes/upload/convert`
- `POST /api/resumes`
- `PUT /api/resumes/{resumeId}/markdown`
- `GET /api/resumes/{resumeId}/export/markdown`

New API:

```text
POST /api/resumes/merge-preview
```

The merge preview API must not write to the database.

## Validation and Error Handling

- Unsupported language values are rejected.
- Empty Markdown cannot be saved.
- Same-language imports prompt merge preview instead of creating a duplicate.
- Merge preview failure does not discard the imported draft.
- Save failures keep current editor content intact.
- Upload conversion failures show the backend error or placeholder Markdown, matching existing behavior.

## Testing

Backend tests:

- Saving a first `zh-CN` resume creates one entry and sections.
- Saving another `zh-CN` resume updates the existing canonical entry instead of creating a second entry.
- `zh-CN` and `en` can coexist for the same tenant.
- Merge preview returns merged Markdown and does not write `ResumeEntries`.
- Replace/save still regenerates sections and vectors.

Shared contract tests:

- DTOs expose language fields.
- Merge preview request/response contracts are serializable.

Blazor tests:

- Workspace route exists.
- Workspace references Markdown editor bridge.
- Workspace includes language selector, library, editor, and outline.
- Existing upload/edit routes remain reachable or redirect intentionally.

Manual verification:

- Import a Chinese Markdown resume and save.
- Import a second Chinese resume, generate merge preview, edit, save, and confirm only one Chinese resume remains.
- Import an English resume and confirm it creates a separate canonical resume.
- Confirm sections and vectors exist after save.

## Open Decisions

No open decisions remain for the first implementation.

Chosen decisions:

- Use unified Resume Workspace.
- Use automatic language detection with manual override.
- Support only `zh-CN` and `en`.
- Require merge preview before overwriting an existing same-language resume.
- Prefer static vendor Markdown editor integration.
