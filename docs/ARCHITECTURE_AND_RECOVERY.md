# Architecture, recovery and host-state rules

This is the project's engineering guardrail. It is not a request to change Codex's global permissions. The product may perform strong operations **when an end user explicitly selects them**, but test runs must not indiscriminately alter the developer's active Windows session.

## A. Trust and execution boundaries

Use least privilege for deployed product components wherever viable. Read-only telemetry and hardware inventory should not need persistent administrative execution. UAC-elevated operations belong to a minimal worker/service with precise operations, structured validation and authenticating IPC. Avoid an IPC endpoint that accepts arbitrary command lines, unchecked paths or shell snippets.

Potential components:
- UI process: chart rendering, profiles, dashboard, visible restore controls.
- Background gaming controller: observes defined game life cycles and applies/removes profiles.
- Privileged operation host: controlled operations (process suspend/resume, SCM changes, supported power actions).
- Independent recovery watchdog: monitors worker health/time limits, owns restoration strategy, can restart after crash with durable journal.

Document startup ordering, failures, update/uninstall semantics and location/permissions of logs and journal.

## B. Classifying targets for Extreme Gaming

Classification order, fail-closed:

1. `CRITICAL` — OS session, kernel-adjacent or shared host functionality. Never touch automatically.
2. `PROTECTED` — security/anti-cheat/AV, protected processes and device-critical helpers. Never bypass protections or touch automatically.
3. `GAME_DEPENDENCY` — game, launchers needed for running game, voice chat/stream/overlays explicitly used, linked subprocesses, network/audio stack. Keep.
4. `UNKNOWN` — unknown signed/unsigned/unclassified process, unusual service or ambiguous owner. Default **do not freeze**.
5. `ELIGIBLE_USER_APP` — user-owned standalone process, proven reversible and explicitly allowlisted/selected for freeze.

Treat signer/path/hash/ownership as helpful signals, not as unconditional proof. Detect protected process status and service hosting; prefer application-level opt-in and test-run selection. Multiple processes of one app may share resources. Keep the optimizer, its watchdog, its IPC dependencies and the Windows shell responsive.

## C. Freeze lifecycle and pitfalls

Freezing is suspending execution, **not** a promise of freeing resident/committed memory. Suspended processes can hold locks, sockets and shared system resources; therefore allow only eligible targets, favor user previews, enforce time limits and watchdog recovery.

- Identify target by PID + verified process creation time + immutable identity where possible. Prevent PID-reuse confusion.
- Record original relevant state first; flush journal atomically.
- Apply freeze through chosen mechanism with resource handle ownership, error checks and supported-platform fallbacks.
- Track exactly which suspend references/threads this app has incremented. Avoid unintentionally undoing a suspension created by another tool.
- Confirm freeze took effect or report partial status; never display false success.
- While frozen, show accessible Restore All, per-app Resume and a session deadline.
- On exit/failure, restore only changes owned by the optimizer, in reverse dependency-aware order.
- Handle process exit, start during game, lock contention, nonterminating game process, resume failure, denial of access, session switch, sleep/wake, update/crash and multi-session users.
- Avoid undocumented system-wide suspension. Do not disable Windows Defender or security protections.
- For a fully reliable restart strategy, explain which suspended states are recoverable after the suspending process disappears; implement according to actual API semantics and tests, not assumption.

## D. Recovery journal / write-ahead state

Consider durable records keyed by operation and exact entity identity:

```json
{
  "schema_version": 1,
  "operation_id": "uuid",
  "session_id": "uuid",
  "kind": "temporary_process_suspend",
  "target": {"pid": 1234, "creation_time_utc": "...", "image_path": "..."},
  "prior_state": {},
  "requested_state": {"suspended": true},
  "status": "prepared",
  "created_at_utc": "...",
  "recovery_deadline_utc": "..."
}
```

Statuses: `prepared → applied → reverting → reverted` or `error_needs_attention`. Audit results with timestamps. Use atomic file replacement or transaction-safe embedded DB; ensure journal itself does not claim unsupported guarantees. Recovery must be **idempotent**: repeated startup should not over-resume or corrupt state. A stale journal entry must not act on a different process that reused a PID.

The watchdog must be outside any process the engine could freeze and independent of UI responsiveness. On startup, reconcile pending records before beginning new optimization. Provide emergency action and clear manual recovery instructions in the application's actual docs.

## E. Driver workflow trust

**Discovery:** supported PnP APIs, current driver details and problem codes. A 'missing driver' means an actual problem device with credible evidence, not every optional software component.

**Matching:** Hardware IDs, compatible IDs, OS architecture/build, INF ranking, publisher, device class, model/OEM preference. Avoid naive string version ordering across vendors.

**Recommendations:** every item includes evidence (official URL, vendor, target device, compatible OS, known package version, timestamps, confidence). Preserve distinction between exact package matches and broad advice.

**Acquisition/install:** verify domain and TLS, package signature and applicability, archive extraction safety and installer trust. Only perform after explicit user action; offer Windows Update/OEM UI handoff when a reliable download API does not exist. Consider malware and tampering risks even for signed packages. No third-party mass driver packs or firmware updates in initial version.

**Backup:** `pnputil /export-driver` can back up third-party driver packages, but not guarantee all OEM installers, firmware or a full rollback; document boundaries. Restore points are supplementary, not the sole recovery mechanism.

## F. Test-only policies

- Development can enumerate hardware/services/processes read-only.
- Create isolated fake game processes and fake background workloads to test start/stop and freeze/resume, on Windows when available.
- Unit/integration tests for operations should use mocks/fakes with fault injection; no system-wide service disable, real driver install, AV disabling or blanket suspend during builds.
- If a test requires privileged or host-altering action, mark `manual opt-in test` and skip by default. Give reproducible steps, expected effects and rollback procedure.
- Never silently change the current machine's UAC, protection, update policies, boot configuration or persistent services to satisfy tests.

## G. Performance correctness

Do not confuse process RAM metrics (private bytes, working set, commit), cache/standby memory and actual available RAM. For before/after experiments, record baseline conditions and variability. Avoid performance improvements caused only by killing background processes for benchmark rather than normal usage; explain trade-offs. Idle telemetry should scale down when minimized to tray. Protect private local inventory data.

## H. Optional CPU sensor isolation (2026-10-16)

CPU readings use an independent unprivileged Measure child and validated CpuSnapshot frames, separate from ADLX and the restoration watchdog. Missing SDK/driver, incompatible version, failed initialization and stale data are explicit capability states. The adapter loads only reviewed public SDK3.1.1.5478 DLLs after hash/version/AMD-publisher checks, initializes CPU-only and binds no BIOS/tuning interface. It never installs/starts a driver or elevates. Native success/live CPU values remain unvalidated on this SDK-missing host; see CPU_SENSORS.md. Saving background-app choices now has a dedicated session-page dialog and changes only exact per-profile approvals; runtime engine protection checks remain independent.

## I. Explicitly connected elevated CPU reader (2026-10-16)

The authorized SDK installation supersedes H's earlier missing-component validation gate. Native SDK3.1.1.5478 now returns real5700X readings. Normal discovery reports AccessRequired; only an explicit Monitoring action or developer --cpu-admin flag requests RunAs for a separate Measure --cpu-pipe process. WPF/Host remain ordinary-user. CPU-only native initialization binds no tuning or driver controls. Per-user SID DACL, single-instance random pipe, PID/creation checks, fixed heartbeat, Identification SQOS, native-call watchdog and6h maximum lifetime constrain the connection. SDK DLLs remain external/version/hash/publisher checked. SameUserPipe also fixes the existing Admin broker's .NET CurrentUserOnly owner-SID mismatch. No persistent privileged service/task or driver ACL changes. Actual CPU/elevated pipe/normal UI/fresh frames/disconnect exit now validated; alternate CPUs/OS/SDK versions and driver-install VM gates remain open.
