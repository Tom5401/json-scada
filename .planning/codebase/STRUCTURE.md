# Codebase Structure

**Analysis Date:** 2026-03-16

## Directory Layout

```text
json-scada/
├── src/                    # Application and service modules (drivers, API, UI, tools)
├── conf/                   # Active runtime config (json-scada.json and local runtime settings)
├── conf-templates/         # Template config files for deployments
├── demo-docker/            # Docker compose demo topology and seeded data
├── platform-ubuntu-2404/   # Ubuntu service/process configs and install scripts
├── platform-rhel9/         # RHEL/Rocky service/process configs and install scripts
├── platform-windows/       # Windows platform support files
├── sql/                    # SQL DDL plus SQL-file processing scripts for historian bridge
├── docs/                   # Architecture, schema, install, and developer documentation
├── svg/                    # SVG displays/assets served by web stack
├── bin/                    # Compiled runtime binaries/artifacts (deployment target)
├── log/                    # Runtime log output directory
└── .planning/codebase/     # Generated architecture/quality/stack mapping docs
```

## Directory Purposes

**`src/`:**
- Purpose: Primary source tree for runtime services and protocol components.
- Contains: Node services (`index.js`), Go modules (`go.mod`), C# projects (`Program.cs`, `.csproj`), UI frontend projects.
- Key files: `src/server_realtime_auth/index.js`, `src/cs_data_processor/cs_data_processor.js`, `src/calculations/calculations.go`, `src/AdminUI/package.json`.

**`conf/`:**
- Purpose: Runtime bootstrap configuration used by processes.
- Contains: `json-scada.json` and deployment-specific runtime config.
- Key files: `conf/json-scada.json`, `conf/README.md`.

**`platform-ubuntu-2404/` and `platform-rhel9/`:**
- Purpose: Production-style service manager and web/database configuration for Linux targets.
- Contains: `supervisord.conf`, per-process `.ini` units, nginx/postgresql/mongodb/telegraf configs.
- Key files: `platform-ubuntu-2404/supervisord.conf`, `platform-ubuntu-2404/server_realtime_auth.ini`, `platform-rhel9/supervisord.conf`.

**`demo-docker/`:**
- Purpose: Reproducible container topology for demo/testing.
- Contains: `docker-compose.yaml`, seeded DB folders, mapped config/log/sql volumes.
- Key files: `demo-docker/docker-compose.yaml`, `demo-docker/README.md`.

**`sql/`:**
- Purpose: Historian SQL schema/maintenance and queue-consumer scripts.
- Contains: scripts that ingest generated SQL files to PostgreSQL.
- Key files: `sql/process_pg_hist.sh`, `sql/process_pg_rtdata.sh`.

**`docs/`:**
- Purpose: Canonical project documentation and data model references.
- Contains: architecture diagrams, schema docs, install and developer guidance.
- Key files: `docs/schema.md`, `docs/JSON-SCADA_Arquitecture.txt`, `docs/DEVELOPER_GUIDE.md`.

## Key File Locations

**Entry Points:**
- `src/server_realtime_auth/index.js`: Realtime authenticated HTTP API server.
- `src/cs_data_processor/cs_data_processor.js`: Change-stream processing worker.
- `src/calculations/calculations.go`: Cyclic calculation engine.
- `src/mqtt-sparkplug/index.js`: MQTT/Sparkplug driver runtime entry.
- `src/telegraf-listener/index.js`: UDP JSON listener entry.
- `src/OPC-UA-Client/Program.cs`: OPC UA client process entry.
- `src/lib60870.netcore/iec104client/Program.cs`: IEC104 client process entry.

**Configuration:**
- `conf/json-scada.json`: Global MongoDB bootstrap config consumed by multiple processes.
- `platform-ubuntu-2404/supervisord.conf`: Multi-process orchestration on Ubuntu installs.
- `demo-docker/docker-compose.yaml`: Container orchestration for demo profile.
- `platform-ubuntu-2404/json_scada_http.conf`: Reverse-proxy and access-control policy.

**Core Logic:**
- `src/cs_data_processor/`: Central realtime normalization and historian SQL emission.
- `src/server_realtime_auth/app/`: Auth, middleware, and API support modules.
- `src/cs_custom_processor/`: User-extensible processing module framework.
- `src/lib60870.netcore/`, `src/dnp3/`, `src/iec61850_client/`, `src/OPC-UA-Client/`: Protocol-specific logic.

**Testing:**
- Not centralized at repository root; test/example projects are mainly embedded in protocol/library subtrees.
- Example locations: `src/libiec61850/dotnet/tests/`, `src/libiec61850/dotnet/*example*/`, `src/libiec61850/dotnet/core/2.0/*example*/`.

## Naming Conventions

**Files:**
- Node service entrypoints commonly use `index.js` (example: `src/server_realtime_auth/index.js`).
- C# service entrypoints commonly use `Program.cs` in per-driver directories (example: `src/S7CommPlusClient/Program.cs`).
- Supervisor process definitions use `<service>.ini` in platform directories (example: `platform-ubuntu-2404/mqtt-sparkplug.ini`).

**Directories:**
- Top-level service/driver modules are mostly kebab-case (`src/server_realtime_auth/`, `src/telegraf-listener/`) with some PascalCase legacy modules (`src/AdminUI/`, `src/S7CommPlusClient/`).

## Where to Add New Code

**New Protocol/Acquisition Feature:**
- Primary code: add module under `src/<driver-name>/` following existing per-driver pattern.
- Registration/config path: integrate instance/connection behavior with `protocolDriverInstances` and `protocolConnections` model documented in `docs/schema.md`.
- Process wiring: add supervisor `.ini` under platform directories and optional compose service in `demo-docker/docker-compose.yaml`.

**New Processing Feature:**
- Realtime normalization extensions: `src/cs_data_processor/`.
- Custom business logic or low-latency custom streams: `src/cs_custom_processor/`.
- Cyclic formulas/compiled calculations: `src/calculations/`.

**New API/Backend Service:**
- HTTP/API integrations: extend `src/server_realtime_auth/` or add sibling service in `src/` with its own `index.js` and `README.md`.
- Expose via Nginx proxy in `platform-ubuntu-2404/json_scada_http.conf` (and equivalent platform config).

**New UI Component/Module:**
- Main UI: `src/AdminUI/src/`.
- Custom dashboards/modules: `src/custom-developments/`.
- SVG display assets: `svg/` and editor-related modules under `src/svg-display-editor/` or `src/svgedit/`.

**Utilities:**
- Shared operational scripts: place shell scripts in `sql/` or platform-specific folders depending on scope.
- Reusable JS service utilities: colocate under module-local helper files, following existing module encapsulation.

## Special Directories

**`bin/`:**
- Purpose: Runtime binaries output and executable target path.
- Generated: Yes (build/publish outputs).
- Committed: Yes (present in repository, used for packaged/runtime workflows).

**`log/`:**
- Purpose: Runtime process logs.
- Generated: Yes.
- Committed: Directory scaffold is committed; log content is runtime-generated.

**`demo-docker/sql_data/` and `demo-docker/grafana_data/`:**
- Purpose: Container persistence volumes for demo data.
- Generated: Yes (runtime/stateful).
- Committed: Present as directories in repository for volume mounting.

**`.planning/codebase/`:**
- Purpose: Machine-generated mapping docs for planning/execution workflows.
- Generated: Yes.
- Committed: Intended to be committed as planning artifacts.

---

*Structure analysis: 2026-03-16*
