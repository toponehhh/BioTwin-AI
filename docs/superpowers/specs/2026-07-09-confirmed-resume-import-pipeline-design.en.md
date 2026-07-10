# Confirmed Resume Import Pipeline Design

**Status:** Approved on 2026-07-09

## Purpose

Resume import must begin only after explicit user confirmation. The BioTwin API owns the complete document-processing pipeline, including inspection, conversion, timeline merge, and structured extraction. The client only submits confirmed files, polls and displays API state, sends user commands, and applies the final result. Third-party conversion remains an API implementation detail. Markdown documents bypass conversion. Users can cancel an active import and receive actionable errors in the same progress dialog.

## Scope

The behavior applies to both import entry points:

- The resume creation wizard waits for **Continue**.
- The advanced workspace waits for an explicit **Import** command.

Selecting a file may read and validate it locally, but it must not call an API, upload content, convert content, merge resumes, or invoke extraction.

## Pending Selection

The client keeps each selected file in a `PendingResumeUpload` model containing its name, MIME type, size, and bytes. This makes replacement, cancellation, and retry reliable without depending on a stale browser file handle.

The creation wizard shows one pending file with Replace. The workspace shows a pending list where users can remove files before importing. Existing file-size and empty-file validation runs locally.

## Single-User Operation Coordinator

A user may have only one active resume creation or import session across all clients connected to the current API process. The first Continue in the creation wizard and the Import command in the workspace must acquire an operation lease before any processing or editing begins.

`IResumeOperationCoordinator` defines acquire, validate, renew, and release operations. The current `MemoryResumeOperationCoordinator` implementation stores one lease per user in singleton memory. Each lease has an opaque operation ID and expiration time and is renewed by a heartbeat while the client is editing. API-owned background import work renews its own lease. Save and explicit cancellation release the lease. An abandoned client loses its lease after the configured timeout.

If another client attempts to acquire a lease, the API immediately returns `409 Conflict` with error code `resume_operation_in_progress` and the user-facing message that a resume is already being created or imported. The second client does not upload or process any file.

The abstraction is intentionally compatible with a future Redis implementation. The current memory implementation does not coordinate multiple API instances and loses leases on API restart. After a restart, clients must reacquire a lease before they can continue or save; state-token validation still prevents stale data from overwriting newer resumes.

## Resume State Freshness

The API computes an opaque resume-state token from the user's current `ResumeEntries`, ordered by language and ID, including each entry's ID, language, `UpdatedAt`, and source-file hash. Resume list and detail responses expose the current token. A client must send the token it most recently received when acquiring an operation lease and when saving a result.

The API recomputes and compares the expected token before starting an import and again inside the transaction that writes resume data. A mismatch returns `409 Conflict` with error code `resume_state_stale`; the client must refresh resume state before it can retry. Successful save, replace, or delete operations naturally produce a new token through their updated database state.

The existing unique index on `(TenantId, Language)` remains the final database safeguard for one canonical resume per language. Save no longer silently replaces a same-language resume from a stale client.

## Import Stages

The blocking dialog uses the approved compact horizontal stage rail. It shows short stage labels, one current-stage description, and the overall percentage reported by the API.

Creation wizard stages:

1. Upload: 0-15%
2. Inspect: 15-25%
3. Prepare: 25-65%
4. Merge: 65-82%
5. Structure: 82-100%

Workspace stages:

1. Upload: 0-20%
2. Inspect: 20-30%
3. Prepare: 30-80%
4. Draft: 80-100%

Stages that are unnecessary are marked `Skipped` and the overall percentage advances to the next boundary. Examples include Prepare for Markdown files and Merge when no same-language resume exists.

## API Import Pipeline

The client starts a BioTwin import job with a mode of `wizard` or `workspace`. The API stores the complete job state and runs every document-processing stage in the background.

For wizard jobs, the API inspects the file, prepares Markdown, resolves and merges an existing same-language resume when necessary, and extracts the final editable resume structure. For workspace jobs, the API inspects and prepares all confirmed files and returns the editable draft results. The client does not call merge or extraction endpoints as part of import.

The API detects Markdown using the `.md` or `.markdown` extension and the `text/markdown` MIME type. Markdown bytes are decoded as strict UTF-8 and passed directly to later API stages. Invalid encoding produces a user-actionable file error.

Other supported file types are sent by the API to the configured conversion provider. Provider URLs, job IDs, names, messages, and errors never appear in client contracts or UI text.

The public contract becomes a BioTwin-owned `ResumeImportJobDto`. It contains overall progress, the current stage, all displayable stage states, cancellation status, an error code, a recovery hint, and the final wizard or workspace result. Provider progress is mapped into the Prepare stage rather than exposed directly.

## Cancellation

The progress dialog always offers **Cancel import**.

- The client cancels its active upload or polling call through a per-import `CancellationTokenSource` and sends the API cancellation command.
- If a BioTwin job ID exists, the client calls `DELETE /api/resumes/import-jobs/{jobId}`.
- The API owns a cancellation source for the complete job. It verifies tenant ownership, cancels merge and extraction calls, marks the job canceled, ignores later provider results, and returns the canceled state for subsequent reads.
- Explicit cancellation releases the resume operation lease, returns the UI to pending selection, and retains the selected files for retry or replacement. Retry and Choose another file retain and renew the existing lease.

The current conversion provider has no reliable physical cancellation endpoint. A provider thread that has already started may finish in the background, but BioTwin will not poll, accept, or process its result. The API boundary leaves room for physical provider cancellation later.

## Error Experience

Errors remain inside the blocking dialog. The horizontal rail marks the failed stage and the dialog displays:

- a short user-facing reason;
- one concrete recovery instruction;
- **Retry** for recoverable failures;
- **Choose another file** for invalid or unsupported files;
- **Cancel import** for every failure.

No stack traces, provider names, internal endpoints, or raw provider messages are shown. Cancellation is a neutral outcome, not an error.

## Client Responsibilities

The client does not calculate document-processing progress and does not orchestrate conversion, merge, or extraction. It is responsible only for pending file selection, explicit confirmation, acquiring and renewing the operation lease, uploading confirmed files, polling `ResumeImportJobDto`, rendering the API-provided stages and percentage, issuing cancel or retry commands, and applying the completed result to the screen. The creation wizard advances only after the API job reaches 100%. The workspace displays the API-managed batch and current-file state.

On `resume_operation_in_progress`, the dialog explains that another client already owns the resume session. On `resume_state_stale`, the dialog instructs the user to refresh before retrying and does not start an import.

## Testing

Coverage must include:

- file selection makes no API request;
- Continue and Import start their respective pipelines;
- concurrent operation acquisition for the same user returns `409 Conflict`;
- an expired in-memory lease can be acquired safely and an active lease cannot;
- lease heartbeat, save, cancellation, and timeout update lease state correctly;
- a stale expected resume-state token is rejected before upload, merge, or save;
- successful resume mutations produce a different state token;
- Markdown bypasses the external HTTP handler;
- the API, rather than the client, invokes merge and structured extraction for wizard imports;
- provider messages and names do not reach client DTOs;
- API job progress maps to the correct overall stage and percentage;
- skipped stages render correctly;
- cancellation stops client work and marks an owned API job canceled;
- one tenant cannot cancel another tenant's job;
- retry and choose-another-file actions preserve the expected state;
- errors render recovery guidance in the dialog;
- the client import path contains no merge or extraction orchestration calls;
- existing import, merge, extraction, save, and navigation tests remain green.

## Out of Scope

- Persisting unfinished import drafts.
- Physically terminating the current provider's conversion thread.
- Parallel workspace imports.
- Exposing provider-specific diagnostics to the browser.

## Database Impact

This feature does not change database schema and requires no SQL migration. Existing `ResumeEntries` timestamps, source hashes, and the `(TenantId, Language)` unique index provide the persisted inputs and final uniqueness safeguard. API startup schema-validation behavior remains unchanged.
