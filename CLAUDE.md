# JetApi: notes for Claude

Fast, local-first, Git-friendly desktop client for REST and gRPC API testing (a lighter Postman alternative).
Users: the owner, developers and testers. Open source (MIT), used in-house. Docs are Turkish; code, comments and
commit messages are English. Talk to the owner in Turkish.

## Read first

- `docs/vision.md`: principles. When in doubt, correctness comes first, then speed, local-first, Git-friendly.
- `docs/requirements.md`: requirement IDs and status. `docs/roadmap.md`: milestones (M1 mostly done, M2 next).
- `docs/architecture.md`, `docs/adr/`: decisions. Write a new ADR for any hard-to-reverse decision.
- `docs/file-format.md`: the YAML workspace format. Any change here needs `schemaVersion` handling.
- `docs/ui-design.md`: the approved UI. Design prototype: https://claude.ai/artifact/N4vd7UoR9GwYreS2QypZfd

## Commands

```bash
dotnet build JetApi.slnx
dotnet test --solution JetApi.slnx                     # all tests (Microsoft Testing Platform, xUnit v3)
dotnet test --project tests/JetApi.Desktop.Tests       # one project
dotnet format JetApi.slnx --verify-no-changes          # CI fails on formatting
dotnet run --project src/JetApi.Desktop                # run the app
build/package.sh win-x64                               # self-contained package into dist/ (docs/packaging.md)
build/measure.sh win-x64                               # start-up time and idle memory (docs/performance.md)
```

The UI screen tests save screenshots when `JETAPI_SCREENSHOTS=<folder>` is set. Look at them after UI changes.

## Layout

- `src/JetApi.Core`: immutable domain model, variable resolution, auth inheritance, `CollectionTree`. No I/O.
- `src/JetApi.Storage`: YAML workspace read/write (`WorkspaceStore`, `RequestFiles`).
- `src/JetApi.Http`: `HttpRequestPreparer` (pure) and `HttpRequestExecutor` (timing, error kinds).
- `src/JetApi.Grpc`: runtime `.proto` loading (protobuf-net.Reflection, `extern alias pbnet`), `ProtoJsonCodec`, `GrpcInvoker`.
- `src/JetApi.Runner`: collection runs (order, iterations, CSV/JSON data rows, delay, stop on failure, run variables);
  sending is behind `IRequestSender` (the app's `RunSender`; the CLI's own). ADR-0009.
- `src/JetApi.Testing`: response checks and extractions (`tests`/`extract` in request files), our own RFC 9535 JSONPath,
  JSON Schema via LateApexEarlySpeed (ADR-0008; the json-everything packages need a maintenance fee, do not add them).
- `src/JetApi.Desktop`: Avalonia 12 + CommunityToolkit.Mvvm. `Services/`, `ViewModels/` (UI-free, unit tested), `Views/`,
  `Themes/Tokens.axaml` (all colors and icons, light and dark), `Themes/Styles.axaml`, `Localization/Strings*.resx`.
- `tests/*`: one test project per source project. The gRPC codec is cross-checked against Google's generated code.

## Rules

- Warnings are errors; .NET analyzers are at `latest-recommended`. Suppress a rule only with `SuppressMessage` and a justification.
- Package versions live only in `Directory.Packages.props`. Lock files are committed, and CI restores in locked mode.
  Target runtimes are listed in `src/Directory.Build.props`, so publishing with `-r <rid>` does not change lock files.
  `nuget.config` restores only from nuget.org.
- Every UI string goes in both `Strings.resx` (EN) and `Strings.tr.resx` (TR). A test checks parity and that every used key exists.
  Use `{l:Tr Key}` in XAML and `Loc.Get` / `Loc.Format` in code. Engine projects (`Http`, `Grpc`, `Storage`, `Import`, `Testing`, `Runner`) have their own
  `Messages.resx` / `Messages.tr.resx` and an internal `Messages.Get/Format`; their tests pin en-US.
- Colors only via `{DynamicResource ...}` tokens, never hard-coded, so the theme switches live.
- No element-name or `$parent` bindings inside item templates: put commands on the item view model
  (see `KeyValueRowViewModel.RemoveCommand`). The screen tests fail on binding errors.
- User-input problems are returned as result objects with clear messages, not exceptions.
- Headless UI tests: dispatch a `Func<Task<T>>` onto `HeadlessApp.Session`. Never block with `.GetResult()` on the UI thread.
- Conventional commits (`feat(desktop): ...`). Update `CHANGELOG.md` and the docs with user-facing changes.

## Open work (see docs/roadmap.md)

M1 and M2 are done; `v0.2.0` was tagged on 2026-11-09. Only Windows packages are needed (macOS/Linux optional).

Next, M3 (test automation): no-code assertions and extracting variables from responses (ADR-0005), a collection/folder
runner, data-driven runs (CSV/JSON), the `jetapi` CLI with JUnit XML and HTML reports, importing Postman `pm.test`.

Notes:
- CI (`ci.yml`) runs only on pushes to `main`, but this repo has no `main` (the default branch is
  `ccr-2a71755b-czvby1`), so CI does not run on pushes; run the full test suite locally before pushing.
- NFR-1 (start-up < 1.5 s) is borderline: 1.5-2.0 s on 2026-11-09 depending on machine load, the same for 0.1.0-alpha.1
  and 0.2.0; idle memory meets NFR-2 (158 MB). Measure with `build/measure.sh win-x64` on an idle machine;
  `--measure` ignores the saved rendering setting. See docs/performance.md.
