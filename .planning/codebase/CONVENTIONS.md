# Coding Conventions

**Analysis Date:** 2026-03-16

## Naming Patterns

**Files:**
- C# driver files use PascalCase names for classes/modules, e.g. `src/S7CommPlusClient/TagsCreation.cs`, `src/S7CommPlusClient/MongoCommands.cs`.
- Go services use lowercase snake_case or lowercase single-word file names, e.g. `src/calculations/calculations.go`, `src/i104m/i104m.go`.
- TS/JS in `log-io` commonly use lowercase single-word files, e.g. `src/log-io/server/src/index.ts`, `src/log-io/server/src/server.ts`, `src/log-io/inputs/file/src/input.ts`.
- Vue UI entry points use conventional Vue naming, e.g. `src/AdminUI/src/main.js`, `src/AdminUI/src/App.vue`.

**Functions:**
- C#: PascalCase for methods (`newRealtimeDoc` is a notable mixed-style exception) and camelCase fields for mapped document members in `src/S7CommPlusClient/TagsCreation.cs`.
- Go: camelCase for functions and variables (`readConfigFile`, `mongoConnect`, `processRedundancy`) in `src/calculations/calculations.go`.
- TS/JS: camelCase for functions and variables (`loadConfig`, `broadcastMessage`, `initApp`) in `src/log-io/server/src/server.ts`, `src/AdminUI/src/main.js`.

**Variables:**
- Constants are uppercase in TS/JS modules (`CONFIG_PATH`, `ROOT_PATH`, `UI_BUILD_PATH`) in `src/log-io/server/src/index.ts` and `src/log-io/inputs/file/src/index.ts`.
- Go constants are lowerCamel with type annotation in some modules (`softwareVersion`, `processName`) in `src/calculations/calculations.go`.

**Types:**
- TS type aliases use PascalCase (`ServerConfig`, `MessageHandlers`) in `src/log-io/server/src/types.ts`.
- C# classes use PascalCase (`rtData`, `protocolDestination` are lowercase exceptions tied to document schema names) in `src/S7CommPlusClient/TagsCreation.cs`.

## Code Style

**Formatting:**
- Prettier is configured in many Node subprojects via `.prettierrc.json` files, including `src/AdminUI/.prettierrc.json` and `src/log-io/.prettierrc.json`.
- Shared Prettier signals: `semi: false`, `singleQuote: true`, `tabWidth: 2`, `printWidth: 80`, `endOfLine: "lf"`.
- No single root formatter config detected; formatting is per-subproject.

**Linting:**
- `src/AdminUI/.eslintrc.js` extends `plugin:vue/vue3-essential` and `eslint:recommended`.
- `src/log-io/server/.eslintrc` and `src/log-io/inputs/file/.eslintrc` use `@typescript-eslint/parser`, `airbnb-base`, and `plugin:@typescript-eslint/recommended`.
- Strict lint gate exists in `log-io` packages: `eslint --max-warnings 0 'src/**/*.ts'` in `src/log-io/server/package.json` and `src/log-io/inputs/file/package.json`.

## Import Organization

**Order:**
1. Node/core modules first (`fs`, `os`, `path`, `net`) in `src/log-io/server/src/index.ts` and `src/log-io/server/src/server.ts`.
2. Third-party packages next (`express`, `socket.io`, `basic-auth`, `vue`, `vuetify`) in `src/log-io/server/src/server.ts`, `src/AdminUI/src/main.js`.
3. Local relative modules last (`./server`, `./types`, `./router`) in `src/log-io/server/src/index.ts`, `src/AdminUI/src/main.js`.

**Path Aliases:**
- Not detected in sampled TS/JS modules; relative imports dominate.

## Error Handling

**Patterns:**
- Go services validate config and terminate fast on invalid startup state using `os.Exit(...)` (`src/calculations/calculations.go`).
- Runtime loops continue with log warnings on transient backend issues (Mongo disconnect/redundancy checks) in `src/calculations/calculations.go`.
- TS modules use top-level async IIFE with `.catch(...)` and `console.error(...)` in `src/log-io/server/src/index.ts` and `src/log-io/inputs/file/src/index.ts`.
- C# drivers rely on typed data models and defaults (`[BsonDefaultValue]`) to reduce null/shape errors in `src/S7CommPlusClient/TagsCreation.cs`.

## Logging

**Framework:**
- Go standard `log` package in backend processes (`src/calculations/calculations.go`).
- TS/JS `console.log`, `console.warn`, `console.error` in `src/log-io/server/src/server.ts`, `src/log-io/server/src/index.ts`.

**Patterns:**
- Operational lifecycle logs for startup/listen events in `src/log-io/server/src/server.ts`.
- Conditional debug logging keyed off config (`config.debug`) in `src/log-io/server/src/server.ts`.
- Redundancy/health logs in long-running Go process loops in `src/calculations/calculations.go`.

## Comments

**When to Comment:**
- Comments are used to explain protocol/domain behaviors and startup assumptions, e.g. split-message parsing and config discovery in `src/log-io/server/src/server.ts`, `src/log-io/server/src/index.ts`.
- License and module headers are common in C# and Go files (`src/S7CommPlusClient/TagsCreation.cs`, `src/calculations/calculations.go`).

**JSDoc/TSDoc:**
- Light JSDoc usage in TS modules (`src/log-io/server/src/server.ts`); no pervasive TSDoc enforcement detected.

## Function Design

**Size:**
- Mixed: utility wrappers are small in TS (`loadConfig`), while protocol/data mapping functions can be very large in C# (`newRealtimeDoc` and document models in `src/S7CommPlusClient/TagsCreation.cs`).

**Parameters:**
- TS uses typed object parameters and explicit function signatures (`ServerConfig`, handler signatures) in `src/log-io/server/src/types.ts`.
- Go and C# frequently pass config/doc structs to keep function signatures domain-oriented (`cfg config`, `S7CPValue ov`).

**Return Values:**
- TS async functions return `Promise<void>` in server/input entry points.
- Go follows explicit `(value, err)` tuple pattern for integrations (`mongoConnect`) in `src/calculations/calculations.go`.

## Module Design

**Exports:**
- TS server modules prefer default export for main entry function (`export default main`) in `src/log-io/server/src/server.ts`.
- Type modules use named exports (`ServerConfig`, `MessageHandlers`) in `src/log-io/server/src/types.ts`.

**Barrel Files:**
- Not a dominant pattern in sampled areas.

## Quality Guardrails

- Subproject-level lint gates exist for select TS packages (`src/log-io/server/package.json`, `src/log-io/inputs/file/package.json`, `src/AdminUI/package.json`).
- Type safety guardrails exist in `log-io` packages through `type-check` scripts (`tsc --noEmit`) in `src/log-io/server/package.json` and `src/log-io/inputs/file/package.json`.
- Data-shape guardrails are implemented through extensive BSON defaults/attributes in C# drivers (`src/S7CommPlusClient/TagsCreation.cs`).
- Guardrails are uneven across the monorepo; many Node subprojects have placeholder test scripts and no lint/type-check scripts (`"test": "echo \"Error: no test specified\" && exit 1"` in multiple `src/*/package.json`).

---

*Convention analysis: 2026-03-16*
