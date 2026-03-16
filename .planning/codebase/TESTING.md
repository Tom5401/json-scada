# Testing Patterns

**Analysis Date:** 2026-03-16

## Test Framework

**Runner:**
- React Scripts test runner (Jest under CRA) in `src/log-io/ui/package.json` via `react-scripts test`.
- NUnit 4.3.2 in `src/libiec61850/dotnet/tests/tests.csproj` with test code in `src/libiec61850/dotnet/tests/Test.cs`.

**Assertion Library:**
- Jest `expect(...)` assertions in `src/log-io/ui/src/components/index.test.tsx`.
- NUnit `Assert.*` assertions in `src/libiec61850/dotnet/tests/Test.cs`.

**Run Commands:**
```bash
cd src/log-io/ui && npm test              # Run React/Jest tests (watch-mode capable)
cd src/log-io/server && npm run lint      # TS lint gate (max warnings 0)
cd src/log-io/server && npm run type-check # TS type-check gate
cd src/log-io/inputs/file && npm run lint
cd src/log-io/inputs/file && npm run type-check
cd src/AdminUI && npm run lint            # Vue lint with --fix
```
- `dotnet test` command is not explicitly documented in repo scripts, but `src/libiec61850/dotnet/tests/tests.csproj` is a dedicated NUnit test project and is a candidate validation target.
- `go test` assets were not detected in core Go service directories (`src/calculations`, `src/i104m`).

## Test File Organization

**Location:**
- JS/TS tests are co-located in feature area: `src/log-io/ui/src/components/index.test.tsx`.
- .NET tests are grouped in dedicated test project folder: `src/libiec61850/dotnet/tests/Test.cs`.

**Naming:**
- JS/TS: `*.test.tsx` pattern.
- .NET: NUnit attribute-driven test methods inside `Test.cs`.

**Structure:**
```
src/log-io/ui/src/components/*.test.tsx
src/libiec61850/dotnet/tests/Test.cs
```

## Test Structure

**Suite Organization:**
```typescript
test('behavior description', () => {
  const { getByTestId } = render(<App socket={socket} initialState={state} />)
  fireEvent.click(getByTestId('add-screen-btn'))
  expect(socket.emit).toBeCalledWith('+activate', 'app1|server1')
})
```
(From `src/log-io/ui/src/components/index.test.tsx`)

**Patterns:**
- Setup pattern: build deterministic mock socket with local callback registry.
- Event simulation pattern: use `act(...)` for async state/event updates.
- UI interaction pattern: use `fireEvent` and `getByTestId` selectors.
- Assertion pattern: verify rendered text and emitted socket events.

## Mocking

**Framework:**
- Jest function mocks via `jest.fn()` in `src/log-io/ui/src/components/index.test.tsx`.

**Patterns:**
```typescript
const mockEmit = jest.fn()
const socket = {
  on: (eventName, callback) => { ... },
  trigger: (eventName, data) => { ... },
  emit: mockEmit,
}
```

**What to Mock:**
- Socket transport boundaries and inbound event streams are mocked in UI tests (`src/log-io/ui/src/components/index.test.tsx`).

**What NOT to Mock:**
- Not codified globally. Current pattern keeps real component render behavior while mocking only network/socket edge.

## Fixtures and Factories

**Test Data:**
```typescript
const testInputs = [
  { inputName: 'app1|server1', stream: 'app1', source: 'server1' },
  { inputName: 'app1|server2', stream: 'app1', source: 'server2' },
]
```

**Location:**
- Inline fixtures inside test files (no shared fixtures directory detected) in `src/log-io/ui/src/components/index.test.tsx`.

## Coverage

**Requirements:**
- No global minimum coverage threshold detected.
- No repo-level coverage command detected.

**View Coverage:**
```bash
Not detected as an explicit repo-standard command
```

## Test Types

**Unit Tests:**
- Present in `src/libiec61850/dotnet/tests/Test.cs` for value type behavior and model/server utility behavior.

**Integration Tests:**
- UI interaction tests in `src/log-io/ui/src/components/index.test.tsx` behave as component-level integration tests around socket event flows.

**E2E Tests:**
- Not detected (no Playwright/Cypress config discovered).

## Common Patterns

**Async Testing:**
```typescript
act(() => {
  socket.trigger('+input', testInput)
})
```

**Error Testing:**
```typescript
expect(queryByText('[app2] [server1] - msg789')).toBeNull()
```

## Test Gaps

- Monorepo-wide test coverage is sparse relative to project size: only one JS/TS test file was found (`src/log-io/ui/src/components/index.test.tsx`) and one dedicated .NET test project was found (`src/libiec61850/dotnet/tests/tests.csproj`).
- Many Node subprojects use placeholder test scripts (`"test": "echo \"Error: no test specified\" && exit 1"`) in `src/alarm_beep/package.json`, `src/backup-mongo/package.json`, `src/graphql-server/package.json`, `src/mongowr/package.json`, `src/server_realtime_auth/package.json`, and others.
- Core protocol/service runtimes in C#, Go, and several Node modules do not expose uniform CI-oriented test commands at package/root level.
- No standardized coverage gate or quality gate orchestration command was detected at repo root.

## Validation Commands (Discoverable)

```bash
# Type/lint gates in actively managed TS packages
cd src/log-io/server && npm run type-check && npm run lint
cd src/log-io/inputs/file && npm run type-check && npm run lint
cd src/AdminUI && npm run lint

# UI tests
cd src/log-io/ui && npm test

# Candidate .NET test target (inferred from test project)
cd src/libiec61850/dotnet/tests && dotnet test
```

---

*Testing analysis: 2026-03-16*
