# Codebase Concerns

**Analysis Date:** 2026-03-16

## Tech Debt

**Security defaults and demo credentials in runtime code and config (Severity: Critical):**
- Issue: Production-capable services include insecure defaults (hardcoded JWT secret, default DB credentials, LDAP sample credentials) and demo compose files explicitly disable DB auth.
- Files: `src/server_realtime_auth/app/config/auth.config.js`, `src/server_realtime_auth/index.js`, `demo-docker/docker-compose.yaml`
- Impact: Privilege escalation and unauthorized access risk if defaults are deployed unchanged.
- Fix approach: Enforce fail-fast startup when secrets are missing; move all credentials to environment/secret stores; add secure-by-default config profiles.

**Monolithic authentication/controller module (Severity: High):**
- Issue: Very large controller and API server files combine auth, user/admin operations, process control, import/export, and transport logic.
- Files: `src/server_realtime_auth/app/controllers/auth.controller.js`, `src/server_realtime_auth/index.js`
- Impact: High change risk, hard reviewability, and frequent regression potential during refactors.
- Fix approach: Split by bounded context (`auth`, `users`, `roles`, `project-ops`, `protocol-admin`) with service-layer contracts and integration tests per module.

**Protocol-driver code duplication across stacks (Severity: High):**
- Issue: Similar conversion, ASDU handling, and Mongo bridging logic appears in many protocol clients/servers with parallel implementations.
- Files: `src/lib60870.netcore/iec101client/Common_srv_cli.cs`, `src/lib60870.netcore/iec104client/Common_srv_cli.cs`, `src/lib60870.netcore/iec101server/Common_srv_cli.cs`, `src/lib60870.netcore/iec104server/Common_srv_cli.cs`, `src/S7CommPlusClient/TagsCreation.cs`, `src/OPC-UA-Client/AsduReceiveHandler.cs`
- Impact: Bug fixes must be repeated across copies; behavior drift over time increases incident risk.
- Fix approach: Introduce shared protocol primitives/conversion library and enforce common test vectors for numeric/state conversions.

**Large first-party files exceeding maintainable size (Severity: High):**
- Issue: Multiple files exceed ~1k lines, including core operational paths.
- Files: `src/server_realtime_auth/index.js` (~2470), `src/server_realtime_auth/app/controllers/auth.controller.js` (~1759), `src/cs_data_processor/cs_data_processor.js` (~1420), `src/mqtt-sparkplug/index.js` (~2469), `src/oshmi2json/json-scada-mongo-import.js` (~4391)
- Impact: Slower onboarding, lower confidence edits, and increased merge conflicts.
- Fix approach: Extract cohesive modules, add API-level contracts, and enforce max-file-size lint gates for new code.

## Known Bugs

**Potential path and shell injection vector in project export/import operations (Severity: High):**
- Symptoms: File names are accepted from request payloads and passed into shell-enabled process invocations and filesystem paths.
- Files: `src/server_realtime_auth/app/controllers/auth.controller.js`
- Trigger: Calling `exportProject`/`importProject` with crafted `project.fileName` or `projectFileName` payload.
- Workaround: Restrict endpoints to trusted admin network and sanitize/allowlist filenames externally.

**Shell API module header/content mismatch and mixed responsibilities (Severity: Medium):**
- Symptoms: `shell-api` file contains alarm-beep process header/content patterns, indicating probable copy/paste drift and maintenance confusion.
- Files: `src/shell-api/shell-api.js`
- Trigger: Ongoing maintenance by developers expecting one bounded responsibility from module name.
- Workaround: Treat as unstable module and regression-test behavior after any edit.

## Security Considerations

**Hardcoded JWT and LDAP credentials in source (Severity: Critical):**
- Risk: Credential disclosure and token forgery if defaults remain active.
- Files: `src/server_realtime_auth/app/config/auth.config.js`
- Current mitigation: Optional environment overrides in `src/server_realtime_auth/index.js`.
- Recommendations: Remove defaults from repository; require env secrets at boot; add startup validation and secret-rotation guidance.

**Administrative process control exposed through web API (Severity: High):**
- Risk: Restart/import/export actions invoke OS shell commands; compromise of admin account can pivot to host-level operations.
- Files: `src/server_realtime_auth/app/controllers/auth.controller.js`, `src/server_realtime_auth/app/routes/auth.routes.js`
- Current mitigation: Route-level `authJwt.isAdmin` checks.
- Recommendations: Replace `shell: true` with direct exec args where possible; enforce command allowlists; add audit-grade structured logging and rate limits.

**Legacy frontend assets with dynamic code execution patterns (Severity: High):**
- Risk: Expanded XSS surface via `eval`/`new Function` and old vendored libraries.
- Files: `src/AdminUI/public/websage.js`, `src/AdminUI/public/util.js`, `src/AdminUI/public/lib/jquery-1.5.1.js`, `src/AdminUI/public/lib/jquery-1.7.2.js`
- Current mitigation: Not detected in code-level sanitization at these call sites.
- Recommendations: Remove/replace eval paths, upgrade/remove legacy bundled libs, enforce CSP in serving layer.

## Performance Bottlenecks

**Continuous polling and retry loops in Node workers (Severity: Medium):**
- Problem: Multiple services run perpetual loops with short intervals and reconnect behavior.
- Files: `src/alarm_beep/alarm_beep.js`, `src/shell-api/shell-api.js`, `src/cs_data_processor/cs_data_processor.js`
- Cause: Polling-based architecture with per-service Mongo checks and repeated setInterval loops.
- Improvement path: Consolidate change-stream/event-driven flows; centralize reconnect/backoff policy; add jittered exponential backoff.

**Large single-file parsers/importers (Severity: Medium):**
- Problem: Heavy importer/parser modules in one file can cause memory spikes and difficult profiling.
- Files: `src/oshmi2json/json-scada-mongo-import.js`, `src/mqtt-sparkplug/index.js`
- Cause: Broad responsibilities and limited decomposition.
- Improvement path: Stream processing and staged pipelines with explicit memory bounds.

## Fragile Areas

**Protocol type conversion pipelines (Severity: High):**
- Files: `src/lib60870.netcore/iec101client/Common_srv_cli.cs`, `src/lib60870.netcore/iec104client/Common_srv_cli.cs`, `src/OPC-UA-Client/AsduReceiveHandler.cs`
- Why fragile: Extensive numeric coercion and protocol-specific branching; TODO markers in core branches.
- Safe modification: Use characterization tests with protocol fixture payloads before refactors; preserve serialized field semantics.
- Test coverage: Gaps in protocol-specific automated tests under `src/`.

**Authentication + authorization flow coupling (Severity: High):**
- Files: `src/server_realtime_auth/index.js`, `src/server_realtime_auth/app/controllers/auth.controller.js`, `src/server_realtime_auth/app/middlewares/authJwt.js`
- Why fragile: Tight coupling between token logic, role checks, and operational endpoints.
- Safe modification: Split auth policy evaluation into dedicated module and test role matrix separately.
- Test coverage: Minimal automated backend tests detected.

## Scaling Limits

**Single-instance service assumptions (Severity: Medium):**
- Current capacity: Not explicitly defined in code.
- Limit: Service state and loops are mostly in-process; horizontal scaling semantics for admin operations and worker ownership are unclear.
- Scaling path: Introduce distributed locks/leader election for singleton jobs and externalized job orchestration.

**Mongo/PG local-default assumptions (Severity: Medium):**
- Current capacity: Defaults target local endpoints and demo credentials.
- Limit: Operational hardening and multi-environment deployment complexity increase rapidly.
- Scaling path: Environment profile system with strict production mode requirements.

## Dependencies at Risk

**Legacy vendored JS libraries in Admin UI (Severity: High):**
- Risk: Security vulnerabilities and unmaintained APIs.
- Files: `src/AdminUI/public/lib/jquery-1.5.1.js`, `src/AdminUI/public/lib/jquery-1.7.2.js`, `src/AdminUI/public/lib/jquery-1.8.3.js`, `src/AdminUI/public/lib/modernizr-2.0.6.js`
- Impact: Increased XSS and supply-chain exposure; difficult patching.
- Migration plan: Replace vendored legacy bundles with managed dependencies and pinned modern versions.

## Missing Critical Features

**Automated safety net for core backend/protocol paths (Severity: High):**
- Problem: Very limited test files found relative to backend/protocol surface area.
- Blocks: Safe refactoring of auth, protocol conversion, and import/export operations.

## Test Coverage Gaps

**Backend and protocol clients largely untested (Priority: High):**
- What's not tested: Auth flows, admin action endpoints, protocol conversion correctness, reconnect behavior.
- Files: `src/server_realtime_auth/index.js`, `src/server_realtime_auth/app/controllers/auth.controller.js`, `src/lib60870.netcore/iec101client/Common_srv_cli.cs`, `src/lib60870.netcore/iec104client/Common_srv_cli.cs`, `src/OPC-UA-Client/AsduReceiveHandler.cs`, `src/S7CommPlusClient/TagsCreation.cs`
- Risk: High regression probability during cleanup or security hardening.
- Priority: High

**Frontend test coverage narrow to one detected test module (Priority: Medium):**
- What's not tested: Most AdminUI interactive and legacy-script behavior.
- Files: `src/log-io/ui/src/components/index.test.tsx` (only detected test file under `src/`)
- Risk: UI regressions may ship unnoticed, especially around legacy script integration.
- Priority: Medium

---

*Concerns audit: 2026-03-16*
