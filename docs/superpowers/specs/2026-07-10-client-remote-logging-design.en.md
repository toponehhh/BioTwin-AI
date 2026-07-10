# Client Remote Logging Design

**Status:** Option 1 selected on 2026-07-10; awaiting written-design approval

## Purpose

The BioTwin Blazor client must send important runtime failures to server logs through the BioTwin API. The feature must cover shared API-call failures and unhandled Blazor component exceptions without allowing log-delivery failures to disrupt the client or exposing resume content, authentication data, or other sensitive request information.

## Existing Foundation

The client already registers `RemoteClientLoggerProvider` and sends `ClientLogEntryRequest` to `POST /api/client-logs`. The API `ClientLogsController` can already write these requests through `ILogger` and Serilog.

This work strengthens that path instead of creating a second logging system. The current gaps are that many pages display caught exceptions without writing to `ILogger`, and the root component has no shared error boundary. The current provider also uploads every Information event, which creates unnecessary noise.

## Log Scope

Upload `Warning`, `Error`, and `Critical` by default. Never upload `Debug` or `Trace`. Do not upload ordinary `Information` events; the single explicit exception is the successful client-startup event.

The following conditions must be logged:

- a non-success response from the BioTwin API;
- an unexpected network failure while calling the BioTwin API;
- an unhandled Blazor component exception;
- a non-cancellation exception caught by a critical workflow such as resume import or save when the shared API layer cannot supply enough context.

An `OperationCanceledException` caused by an explicit user cancellation is not uploaded as an error.

## Client Structure

`RemoteClientLoggerProvider` remains the remote output for all `ILogger` instances. It applies severity filtering, recursion protection, length limits, and asynchronous delivery. Delivery uses the existing BioTwin API `HttpClient`, while HTTP categories belonging to the logging infrastructure are excluded to prevent recursive logs when delivery fails.

`ApiClientBase` becomes the shared recording point for API failures. It records the HTTP method, relative path with the query string removed, status code, and exception type. It does not record request bodies, response bodies, or headers. Pages continue to receive the existing user-friendly exception messages.

A dedicated `ClientErrorBoundary` wraps the root router. It records unhandled component exceptions through `ILogger<ClientErrorBoundary>` and renders a recovery view consistent with the existing site. The boundary can recover after successful navigation so one component failure does not permanently break later navigation.

Critical pages log only when they need to add business context, such as an import job ID and current stage. A page must not upload the same failure again when the shared API layer has already recorded it.

## Data and Privacy

Each event may contain:

- severity;
- category name;
- a message that contains no user document content;
- exception type and a length-limited stack trace;
- page path without query string or fragment;
- client UTC timestamp.

Request bodies, response bodies, cookies, authorization headers, passwords, resume Markdown, uploaded file content, profile hashes, and URL query parameters must never be uploaded. Both client and API apply length limits. Category and message limits are conservative; exception stacks may be larger but remain bounded. The API uses structured logging parameters so client text cannot alter the server log template.

## Server Handling

`POST /api/client-logs` remains the only client logging endpoint and stays anonymous so startup failures can be reported. The API validates severity, rejects or ignores events below policy, trims and bounds every string, and writes a Serilog event with a fixed `Client log` marker.

The endpoint always returns `202 Accepted`. If client delivery fails, that event is silently dropped without retry, UI errors, or another remote log event.

## Testing

Automated tests must cover:

- Warning and higher are sent, while ordinary Information, Debug, and Trace are not;
- the startup Information exception is sent;
- logging HTTP categories do not recursively send;
- URL query strings, request bodies, and response bodies are absent from logs;
- messages and exception stacks are length-limited;
- a non-success API response is logged once by the shared client layer;
- user cancellation is not logged as an error;
- unhandled component exceptions are logged by the root error boundary;
- the API controller accepts only policy-approved levels and writes structured logs.

## Non-Goals

This phase does not add batch delivery, a persistent offline queue, a JavaScript `window.onerror`/`unhandledrejection` bridge, browser performance tracing, user analytics, or a third-party telemetry service. Those capabilities can be added independently behind the existing `ILogger` boundary.
