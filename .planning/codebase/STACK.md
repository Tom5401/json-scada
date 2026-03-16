# Technology Stack

**Analysis Date:** 2026-03-16

## Languages

**Primary:**
- JavaScript (Node.js) - Operational services in `src/server_realtime_auth/index.js`, `src/cs_data_processor/cs_data_processor.js`, `src/mqtt-sparkplug/index.js`, `src/OPC-UA-Server/index.js`.
- C# (.NET) - Protocol drivers and clients in `src/OPC-UA-Client/OPC-UA-Client.csproj`, `src/lib60870.netcore/lib60870.netcore.sln`, `src/S7CommPlusClient/S7CommPlusClient.csproj`.
- Go - Compiled processing/drivers in `src/calculations/go.mod`, `src/i104m/go.mod`.

**Secondary:**
- C/C++ - Native protocol/build components in `src/dnp3/Dnp3Server/`, `src/libiec61850/`, `src/mongo-cxx-driver/`.
- TypeScript - MCP and custom processor modules in `src/mcp-json-scada-db/package.json`, `src/cs_custom_processor/tsconfig.json`.
- Vue SFC/CSS - Admin UI in `src/AdminUI/package.json`.

## Runtime

**Environment:**
- Node.js 20+ documented in `README.md`; containerized runtime uses Node.js 22 and Node.js 24 in `demo-docker/docker-compose.yaml` and `Dockerfile`.
- .NET 8 runtime/SDK in `Dockerfile`, `compile-docker/docker-compose.yaml`, and `*.csproj` target frameworks (`net8.0`).
- Go 1.24 toolchain in active modules (`src/calculations/go.mod`, `src/i104m/go.mod`); Docker image also pins Go 1.25.6 in `Dockerfile`.

**Package Manager:**
- npm (multi-package, per-module) with lockfiles in `src/*/package-lock.json`.
- Go modules with lockfiles in `src/calculations/go.sum`, `src/i104m/go.sum`.
- NuGet via .NET SDK (`dotnet restore/build/publish`) in `.github/workflows/lib60870-dotnet-core.yml`.
- apt/apk used for OS and build dependencies in `Dockerfile` and `compile-docker/docker-compose.yaml`.

## Frameworks

**Core:**
- Express 5 (`express`, `@apollo/server`) in `src/server_realtime_auth/package.json`.
- Vue 3 + Vuetify + Vite in `src/AdminUI/package.json`.

**Testing:**
- Not detected as a consistent repository-wide test framework; several Node modules keep placeholder `npm test` scripts in local `package.json` files.

**Build/Dev:**
- Docker/Docker Compose for integrated multi-service runtime in `Dockerfile`, `demo-docker/docker-compose.yaml`.
- Supervisor process manager in `supervisord.conf` and `platform-ubuntu-2404/supervisord.conf`.
- GitHub Actions CI for selected modules in `.github/workflows/*.yml`.

## Key Dependencies

**Critical:**
- `mongodb` / `mongoose` / `MongoDB.Driver` / `go.mongodb.org/mongo-driver` - Core realtime/config persistence across Node, .NET, and Go (`src/server_realtime_auth/package.json`, `src/S7CommPlusClient/S7CommPlusClient.csproj`, `src/calculations/go.mod`).
- `pg` and PostgreSQL client env usage - Historian and SQL-backed integrations (`src/server_realtime_auth/index.js`, `demo-docker/docker-compose.yaml`).
- Protocol libraries: `node-opcua`, OPC Foundation .NET UA client libs, `opendnp3`, PLC4X Go (`src/OPC-UA-Server/package.json`, `src/OPC-UA-Client/OPC-UA-Client.csproj`, `src/dnp3/Dnp3Client/Dnp3Client.csproj`, `src/calculations/go.mod`).

**Infrastructure:**
- MongoDB server (replica set), PostgreSQL/TimescaleDB, Grafana, Telegraf, Nginx managed in `Dockerfile` and `demo-docker/docker-compose.yaml`.
- `@modelcontextprotocol/sdk` for MCP server integration in `src/mcp-json-scada-db/package.json`.

## Configuration

**Environment:**
- Core system connection bootstrap is `conf/json-scada.json` (Mongo URI + database + TLS paths), documented in `conf/README.md`.
- Service-level settings are primarily env-driven in `src/server_realtime_auth/README.md`, `src/grafana_alert2event/README.md`, and platform `*.ini` files under `platform-ubuntu-2404/` and `platform-rhel9/`.
- `.env` files were not detected in top-level scan.

**Build:**
- Container and compilation recipes: `Dockerfile`, `compile-docker/docker-compose.yaml`, `demo-docker/docker-compose.yaml`.
- Native and managed build files: `src/**/*.csproj`, `src/**/go.mod`, `src/**/package.json`.
- CI build definitions: `.github/workflows/calculations-go.yml`, `.github/workflows/lib60870-dotnet-core.yml`, `.github/workflows/i104m-go.yml`, `.github/workflows/dnp3-client-dotnet-core.yml`, `.github/workflows/PLCTagsClient.yml`.

## Platform Requirements

**Development:**
- Supported OS targets include Linux, Windows, and macOS as documented in `README.md`.
- Build dependencies include Docker, .NET SDK 8, Node.js 22/24, Go 1.24+, C/C++ toolchain, cmake, and libpcap/openSSL/system libs (`Dockerfile`, `compile-docker/docker-compose.yaml`).

**Production:**
- Primary deployment model is Linux service stack orchestrated by Supervisor and/or Docker Compose (`supervisord.conf`, `platform-ubuntu-2404/*.ini`, `demo-docker/docker-compose.yaml`).
- Data tier expects MongoDB replica set and PostgreSQL/TimescaleDB availability (`conf/README.md`, `demo-docker/docker-compose.yaml`).

---

*Stack analysis: 2026-03-16*
