# Privacy and Data-Handling Policy

## Status and purpose

- **Status:** Proposed for prototype
- **Applies to:** Capture, local review, redaction, export, retention, and deletion
- **Companion document:** [Threat model](threat-model.md)

This is a product and operating policy for the recorder. It is not a public privacy notice, consent form, legal opinion, or substitute for deployment-specific review.

The recorder creates unusually sensitive evidence. It can collect raw keystrokes, screen content, speech, application audio, accessibility text, and contextual information from outside the tested page. The policy therefore defaults to local processing, explicit channel selection, encryption, limited retention, and redacted export.

## Policy principles

- **Purpose limitation:** State why the session is being recorded before capture.
- **Data minimization:** Enable only channels necessary for that purpose.
- **Participant agency:** Make consent, pause, stop, and withdrawal accessible.
- **Evidence integrity:** Do not alter raw evidence or conceal omissions.
- **Separation:** Keep observed, derived, inferred, and redacted data distinct.
- **Local-first operation:** Do not transmit evidence during capture.
- **Protection by default:** Encrypt active and completed archives.
- **Limited retention:** Set and review an expiry for every session.
- **Controlled disclosure:** Prefer redacted exports and disclose residual risk.
- **Accountability:** Record policy, control boundaries, exports, and deletion actions.

## Roles

- **Study operator:** Defines purpose, required channels, retention, permitted reviewers, and the applicable legal or ethical authority.
- **Auditor:** Operates capture according to the approved policy and responds to participant controls.
- **Participant:** Receives accessible information, chooses optional channels, pauses or stops recording, and uses the deployment's withdrawal process.
- **Archive custodian:** Protects keys, storage, access, retention, and deletion.
- **Reviewer:** Uses evidence only for the stated purpose and protects any export.
- **Software:** Enforces the selected policy and records boundaries. It does not determine whether the study is lawful.

One person may hold several roles, but the archive must record the operating policy rather than personal assumptions.

## Data inventory

| Data category | Examples | Sensitivity | Default |
| --- | --- | --- | --- |
| Raw keyboard | Scan codes, key transitions, modifiers, device | Critical | Required for full interaction analysis, with explicit warning |
| Raw mouse | Movement, buttons, wheel, device | High | Required for full interaction analysis |
| Video | Display, browser, dialogs, notifications, other applications | Critical | Full display proposed, selected window available |
| Microphone | Think-aloud speech, background voices | Critical | Off until enabled |
| System audio | Screen reader, media, calls, notifications | Critical | Off until enabled |
| Process audio | Selected process-tree playback | High | Off until enabled and supported |
| Accessibility state | Names, values, text, selection, focus, structure | Critical | Required for accessibility analysis, bounded |
| Accessibility context | Screen-reader use, interaction methods, accommodations, and possible disability-related information | Critical | Limited to the stated study purpose |
| Window and process context | Titles, paths, product versions, bounds | High | Enabled with redaction support |
| Environment | Displays, devices, locale, software versions | Medium | Enabled |
| Annotations | Auditor notes and labels | High | Enabled when used |
| Diagnostics | Queue pressure, errors, omissions, resource use | Medium | Always enabled, raw payloads prohibited |
| Derived data | Transcripts, correlated events, indexes | Same as source | Created after capture |
| Inferred data | Probable commands, intent, usability interpretations | High | Created after capture and labelled |

A study may mark a normally required channel as disabled. The recorder must then state which research questions cannot be answered rather than coercing consent.

## Pre-session configuration

Before start, the operator must set:

- Study or session purpose.
- Participant-facing description.
- Required and optional channels.
- Full-display or selected-window scope.
- Raw-input scope.
- Process exclusions.
- Audio sources and fallback permission.
- Whether UI Automation text and values are retained.
- Intended reviewers and export type.
- Retention period.
- Archive location.
- Whether portable recovery or export keys are required.

The configuration receives a policy identifier and version. The exact policy shown at consent is stored with the session.

## Accessible consent

Consent presentation must:

- Use ordinary text and controls exposed through UI Automation.
- Be fully operable by keyboard and screen reader.
- Identify every enabled channel in plain language.
- Explain that raw keyboard capture can include passwords and private text.
- Explain whether the whole display or one window is recorded.
- Explain that audio can include other people and unrelated applications.
- Explain that UI Automation can expose text not obvious in the image.
- Explain that assistive-technology use and interaction evidence may reveal accessibility needs or disability-related information.
- Explain storage, encryption, retention, reviewers, and export.
- Explain pause, stop, withdrawal, and contact procedures.
- Avoid preselected optional channels.
- Avoid a time limit.
- Provide a reviewable summary before confirmation.

The recorder stores the policy and acknowledgement event, not a claim that consent satisfies every applicable law. If the deployment relies on another authority, the operator records that basis outside or alongside the session policy.

## Recording visibility and control

While recording, the app must show:

- Recording, pausing, paused, stopping, finalizing, or completed state.
- Enabled channels.
- Full-display or selected-window target.
- Healthy, degraded, failed, fallback, and suppressed channels.
- Elapsed time and archive destination.
- Keyboard-operable pause, stop, and annotation controls.
- The configured emergency-stop command.

Recording state must not rely on color, sound, animation, or a system-tray icon alone. Status updates must be available to screen readers without repeatedly interrupting participant speech.

## Pause and sensitive-entry mode

Global pause stops acquisition of participant evidence. It does not merely add a marker to continuing capture.

While paused:

- Keyboard and mouse records are not acquired.
- Video frames are not acquired.
- Microphone and playback audio are not acquired.
- UI Automation events and snapshots are not acquired.
- Foreground titles and application context are not acquired beyond what is required to detect safe resume.
- Health and control boundaries continue.

Sensitive-entry mode is a fast, accessible pause intended for passwords or private content. The user can invoke it without focusing the recorder. Resume always requires an explicit command.

If UI Automation reliably reports that the focused control is a password field, the recorder suppresses keyboard payloads and records only a sensitive-input suppression interval and aggregate count. This is a secondary protection. Applications may omit, delay, or misreport password state, so the interface must never promise automatic password protection.

## Secure desktop, lock, and session changes

The recorder must suspend participant capture when Windows enters a secure desktop, the workstation locks, the signed-in session disconnects, or the active desktop cannot be verified.

The archive records:

- Last confirmed ordinary-desktop time.
- Suspension reason.
- Whether each collector confirmed its boundary.
- First confirmed ordinary-desktop time after return.
- Any interval whose status is unknown.

Resume after a secure transition requires explicit confirmation when the recorder cannot prove that every channel remained stopped.

## Data minimization rules

- Do not capture clipboard contents in the prototype.
- Do not capture browser history, cookie values, saved passwords, authorization values, request bodies, or response bodies.
- Capture cookie names, non-value attributes, operation type, result, source context, and timing so consent-related cookie behavior can be analyzed.
- Treat DOM, page source, script locations, computed styles, accessibility trees, URLs, titles, and rendered frames as sensitive tested-content evidence.
- Do not record process command lines unless a later requirement and review justify them.
- Store executable hashes and product metadata only when needed for provenance.
- Rate-limit and bound UI Automation snapshots.
- Request only the UI Automation properties required by the active capture profile.
- Do not duplicate raw payloads in logs, health records, errors, or notifications.
- Do not transcribe speech during capture.
- Do not infer behavior during capture.
- Do not retain image thumbnails outside the encrypted archive.

New fields and channels require a documented purpose, sensitivity classification, consent impact, retention effect, and threat review.

## Local storage and encryption

Every active session uses a random per-session encryption key. Structured evidence, media, full manifests, indexes, annotations, diagnostics, derived data, and inferred data are encrypted in authenticated, recoverable chunks.

The local key envelope is protected for the recording Windows user with user-scoped DPAPI. Machine-scoped protection is not used for ordinary archives because it can make the key available to other users on that machine ([Microsoft DPAPI documentation](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata)).

The active directory:

- Is created with access restricted to the recording user.
- Contains no participant name in its path.
- Uses an opaque session identifier.
- Contains only minimal non-sensitive bootstrap metadata in plaintext.
- Is validated before the first participant evidence is accepted.
- Fails closed if key creation, key protection, permissions, or authenticated writing fails.

The recorder must warn that a malicious administrator, kernel compromise, or authorized memory dump can defeat application-level encryption.

## Network policy

No component may transmit evidence during preparation, capture, pause, resume, stop, or finalization.

During those states:

- The capture host has no telemetry, analytics, upload, update, advertising, or remote-support code path.
- The app does not check for updates.
- The app does not load remote images, fonts, help, or web content.
- Session names, device data, diagnostics, and crash details remain local.
- Opt-in Chromium diagnostic logging is disabled by default. When enabled
  through `A11Y_RECORDER_CHROMIUM_LOG_FILE`, the resulting local file may
  contain tested URLs and browsing details, is not session evidence, and must
  be handled as sensitive diagnostic data.
- Opt-in early native bridge tracing through
  `A11Y_RECORDER_BRIDGE_LOG_FILE` records only process identifiers, monotonic
  tick values, startup stages, child process types, and internal errors. It
  must never include bootstrap contents, pipe names, authentication tokens,
  command lines, URLs, or page data.
- Recording remains functional with the network disconnected.

Post-capture upload is outside the capture workflow. It requires an explicit export, an identified destination, and a separate user action.

## Derived and inferred data

Derived and inferred records:

- Never replace raw evidence.
- Use separate paths and identifiers.
- Identify source evidence and analysis version.
- Retain uncertainty and competing interpretations.
- Inherit the highest sensitivity and earliest expiry of their source evidence unless a documented review sets a stricter policy.
- Are included in exports only when selected.

Transcripts are derived data. Inferred screen-reader commands or user intent are inferred data and must never be presented as directly captured facts.

## Redaction

Redaction creates a new derivative export. It never edits or deletes bytes from the source archive.

The workflow must support:

- Time-range removal across all channels.
- Video masking.
- Audio mute or replacement.
- Removal of keyboard payloads.
- Removal or replacement of UI Automation text and values.
- Removal of window titles, process paths, annotations, and environment identifiers.
- Pseudonymous participant identifiers.
- Reviewer notes describing unresolved exposure.

The redaction interface must show which channels overlap the selected interval. A redacted export includes:

- Source archive identifier without participant identity.
- Redaction policy and tool version.
- Streams inspected.
- Transformations applied.
- Streams not inspected.
- Integrity information for exported content.
- Statement that redaction reduces but may not eliminate disclosure risk.

Automated redaction is a suggestion until a human reviewer confirms it.

## Export

The default export is a redacted, encrypted package. Raw export is a separate advanced action.

Before raw export, the app must:

- State that the package can contain credentials, speech, unrelated applications, accessibility text, and bystanders.
- Identify the intended recipient and purpose.
- Require explicit confirmation.
- Create an export manifest.
- Avoid placing the package in a synchronized or public folder without an additional warning.

Portable exports use a key separate from the local DPAPI envelope. The portable key-wrapping and recovery design must pass cryptographic review before implementation.

An export is a copy. Deleting it from the recorder does not delete copies held by recipients, cloud synchronization, backups, or removable media.

## Retention

Each session must have an expiry set before recording. The prototype default is 30 days.

Allowed policy:

- The operator may shorten the period.
- Extending the period requires a recorded reason.
- Derived and inferred data do not automatically extend raw retention.
- A legal or research hold must identify its authority, owner, scope, and review date.
- The app shows upcoming and expired sessions on launch.
- Expired sessions are not deleted silently. The user confirms deletion or records an extension.
- Raw archives should be deleted when the purpose ends, even if the maximum period has not elapsed.

The application does not create an unattended background deletion task in the prototype.

## Deletion

Deletion requires:

1. Confirming the archive and any linked exports.
2. Closing viewer and analysis handles.
3. Destroying application-held key envelopes and recovery material.
4. Deleting archive files and generated indexes.
5. Recording a non-sensitive deletion receipt separately when required.

The interface must explain that ordinary file deletion and cryptographic erasure do not prove removal from backups, synchronized folders, recipient copies, storage snapshots, or forensic recovery. The archive custodian is responsible for those systems.

Participant withdrawal does not automatically determine deletion. The deployment's consent terms, legal basis, research protocol, holds, and rights process govern the outcome. The recorder must support the decision and record what was done without pretending to make it.

## Access and review

- Archives open only for an authorized user with the required key material.
- The app records successful and failed open attempts without copying evidence into logs.
- Review does not alter raw streams.
- The viewer avoids creating plaintext caches.
- Recent-file lists use opaque identifiers rather than participant names.
- Screen-reader announcements avoid reading sensitive evidence automatically.
- Audio does not autoplay.
- Copy, screenshot, and external-player actions present an exposure warning when they create data outside the protected archive.

The prototype does not implement multi-user authorization or centralized role management. Shared review requires an explicit export.

## Incident handling

An incident includes:

- Recording before consent, while paused, or after stop.
- Unexpected credential or private-data capture.
- Unauthorized archive access or export.
- Lost key or archive.
- Network transmission during capture.
- Integrity validation failure.
- Missing or falsified omission data.
- Malware, dependency compromise, or unsigned release.

On detection, the app must:

- Stop or pause capture when continued recording increases harm.
- Preserve non-sensitive diagnostic and integrity information.
- Avoid copying exposed data into an incident report.
- Mark affected archives restricted.
- Identify source, channels, interval, exports, and potential recipients.
- Direct the operator to the deployment's incident contact and procedure.

The deployment owner determines notification, investigation, evidence preservation, and legal response. Potential breach-notification or participant-harm questions require qualified privacy or legal review.

## Participant and data-subject requests

The recorder supports, but does not adjudicate:

- Locating sessions by a separately maintained participant code.
- Exporting a review copy.
- Correcting derived or inferred annotations without changing raw evidence.
- Restricting access.
- Deleting an archive and locally held keys.
- Recording why a request was denied or limited under the deployment's policy.

Do not place a participant's direct identity in the archive filename. The mapping between participant identity and opaque session identifier should be stored separately with stricter access.

## Logs and support bundles

Logs may contain:

- Component versions.
- State transitions.
- Stable error codes.
- Queue and timing summaries.
- Opaque session, collector, stream, and device identifiers.
- Sanitized exception categories.

Logs must not contain:

- Raw keys or text.
- Audio samples or transcripts.
- Images or thumbnails.
- UI Automation names, values, or text.
- Window titles.
- Full file paths containing user names.
- Encryption keys, key envelopes, passphrases, or recovery material.

Support bundles are generated only after capture and show their contents before export.

## Policy validation

Before participant use, test:

- Consent with keyboard, NVDA, JAWS, and Narrator.
- Channel selection and status announcements.
- Start only after acknowledgement.
- Global pause and sensitive-entry mode.
- Recognized and unrecognized password fields.
- Secure-desktop and workstation-lock suspension.
- No network activity during all capture states.
- User-only archive access.
- Encryption and authentication failure behavior.
- Redaction across every channel.
- Raw versus redacted export warnings and manifests.
- Expiry review and confirmed deletion.
- Recovery after app, host, and power interruption.
- Logs and support bundles for sensitive-data leakage.

## Required deployment decisions

Before collecting participant data, each deployment must document:

- Study purpose and authority.
- Required and optional channels.
- Participant information and consent or other basis.
- Treatment of bystanders and incidental data.
- Retention period and extension authority.
- Archive custodian and permitted reviewers.
- Raw-export approval authority.
- Portable-key and recovery procedure.
- Redaction reviewer.
- Withdrawal and data-request process.
- Incident contact and escalation process.
- Backup, synchronization, and endpoint-protection implications.
- Jurisdictions and any required legal, ethics, or institutional review.
