# External Integrations

**Analysis Date:** 2026-03-16

## APIs & External Services

**Industrial Protocol Drivers and Gateways:**
- OPC UA (client and server) - Data acquisition and exposure through `src/OPC-UA-Client/OPC-UA-Client.csproj` and `src/OPC-UA-Server/package.json`.
  - SDK/Client: `OPCFoundation.NetStandard.Opc.Ua.Client`, `node-opcua`
  - Auth: Uses system config (`conf/json-scada.json`) and per-driver connection documents in MongoDB.
- IEC 60870-5-101/104 - Client/server drivers in `src/lib60870.netcore/`.
  - SDK/Client: Internal `lib60870.netcore` projects
  - Auth: Driver/network config from MongoDB + `conf/json-scada.json` bootstrap.
- DNP3 (client/server) - Integrated through `src/dnp3/Dnp3Client/` and `src/dnp3/Dnp3Server/`.
  - SDK/Client: `opendnp3` (`src/dnp3/Dnp3Client/Dnp3Client.csproj`)
  - Auth: Driver connection config from database/config files.
- IEC 61850 - Client in `src/iec61850_client/` with native/managed library integration from `src/libiec61850/`.
- MQTT/Sparkplug B - Pub/sub integration in `src/mqtt-sparkplug/package.json`.
  - SDK/Client: `mqtt`, `sparkplug-client`
  - Auth: Broker/credential details are configured in runtime config collections and/or service settings.
- Telegraf ingestion - UDP JSON listener in `src/telegraf-listener/index.js` with telegraf configs in `platform-ubuntu-2404/telegraf-*.conf`.

**Identity and API Access:**
- LDAP directory integration for authentication enrichment in `src/server_realtime_auth/index.js`.
  - SDK/Client: `ldapts`
  - Auth: `JS_LDAP_*` environment variables (`src/server_realtime_auth/README.md`).
- JWT-based API sessions in `src/server_realtime_auth/index.js`.
  - SDK/Client: `jsonwebtoken`
  - Auth: `JS_JWT_SECRET` env var or fallback config in `src/server_realtime_auth/app/config/auth.config.js`.
- MCP server for AI tooling in `src/mcp-json-scada-db/package.json`.
  - SDK/Client: `@modelcontextprotocol/sdk`
  - Auth: MongoDB access via standard JSON-SCADA config/env (`src/mcp-json-scada-db/README.md`).

## Data Storage

**Databases:**
- MongoDB replica set (primary realtime/config/event store).
  - Connection: `mongoConnectionString`, `mongoDatabaseName` in `conf/json-scada.json`.
  - Client: Node `mongodb`/`mongoose`, .NET `MongoDB.Driver`, Go `go.mongodb.org/mongo-driver`.
- PostgreSQL + TimescaleDB (historian/reporting/Grafana backing DB).
  - Connection: `PGHOST`, `PGPORT`, `PGDATABASE`, `PGUSER`, `PGPASSWORD` seen in `demo-docker/docker-compose.yaml` and `src/server_realtime_auth/README.md`.
  - Client: Node `pg`; native SQL process scripts in `sql/` consumed by `process_pg_hist` and `process_pg_rtdata` containers.

**File Storage:**
- MongoDB GridFS is used for file/blob style storage and retrieval (`GridFSBucket` usage in `src/server_realtime_auth/index.js`, `src/camera-onvif/index.js`).
- Local filesystem is also used for static assets/logs/config (`svg/`, `log/`, `conf/`, container volumes in `demo-docker/docker-compose.yaml`).

**Caching:**
- Dedicated cache service not detected.

## Authentication & Identity

**Auth Provider:**
- Custom auth service with JWT and RBAC in `src/server_realtime_auth/`.
  - Implementation: Local user/role models in MongoDB with optional LDAP-backed user authentication (`src/server_realtime_auth/README.md`, `src/server_realtime_auth/index.js`).

## Monitoring & Observability

**Error Tracking:**
- Third-party APM/error tracking service not detected.

**Logs:**
- Supervisor-managed process logs to filesystem (`supervisord.conf`, `platform-ubuntu-2404/mcp_server.ini`).
- Nginx, MongoDB, PostgreSQL, Grafana logs managed in container/host log paths (`Dockerfile`, `supervisord.conf`).
- Grafana integration for historical dashboards with reverse proxy path support in `src/server_realtime_auth/index.js`.

## CI/CD & Deployment

**Hosting:**
- Docker container and Docker Compose deployments (`Dockerfile`, `demo-docker/docker-compose.yaml`).
- Supervisor-based Linux host deployment profiles (`platform-ubuntu-2404/`, `platform-rhel9/`).
- Firebase Studio development/deployment workflow documented in `platform-nix-idx/README.md`.

**CI Pipeline:**
- GitHub Actions build pipelines for selected components in `.github/workflows/*.yml`.

## Environment Configuration

**Required env vars:**
- Core web/auth service: `JS_IP_BIND`, `JS_HTTP_PORT`, `JS_CONFIG_FILE`, `JS_AUTHENTICATION`, `JS_JWT_SECRET` (`src/server_realtime_auth/README.md`).
- LDAP integration: `JS_LDAP_ENABLED`, `JS_LDAP_URL`, `JS_LDAP_BIND_DN`, `JS_LDAP_BIND_CREDENTIALS`, `JS_LDAP_SEARCH_BASE`, `JS_LDAP_SEARCH_FILTER`, plus TLS/group mapping variables (`src/server_realtime_auth/README.md`).
- PostgreSQL libpq variables: `PGHOST`, `PGPORT`, `PGDATABASE`, `PGUSER`, `PGPASSWORD` (`src/server_realtime_auth/README.md`, `demo-docker/docker-compose.yaml`).
- Grafana webhook bridge: `JS_ALERT2EVENT_IP_BIND`, `JS_ALERT2EVENT_HTTP_PORT`, `JS_ALERT2EVENT_USERNAME`, `JS_ALERT2EVENT_PASSWORD` (`src/grafana_alert2event/README.md`).
- MCP transport and endpoint: `MCP_TRANSPORT`, `BIND`, `PORT` (`src/mcp-json-scada-db/README.md`).

**Secrets location:**
- Primary operational secrets and connection credentials are expected in `conf/json-scada.json` and deployment-specific `platform-*/` service env/config files.
- Optional auth defaults exist in `src/server_realtime_auth/app/config/auth.config.js` and should be overridden in real deployments.

## Webhooks & Callbacks

**Incoming:**
- Grafana webhook endpoint for alert ingestion (HTTP POST) at `/grafana_alert2event` in `src/grafana_alert2event/README.md`.
- Realtime HTTP API ingress in `src/server_realtime_auth/index.js` (includes `/Invoke/`, `/GetFile`, `/queryJSON`).

**Outgoing:**
- Outbound protocol sessions initiated by drivers to external endpoints (MQTT brokers, OPC UA servers/clients, IEC104 peers, LDAP server) are configured through database-backed driver connection records and service env/config (`src/server_realtime_auth/app/models/protocolConnection.model.js`, `src/mqtt-sparkplug/`, `src/OPC-UA-Client/`, `src/lib60870.netcore/`, `src/server_realtime_auth/index.js`).

---

*Integration audit: 2026-03-16*
