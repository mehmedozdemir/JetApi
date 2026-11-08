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
- `src/JetApi.Desktop`: Avalonia 12 + CommunityToolkit.Mvvm. `Services/`, `ViewModels/` (UI-free, unit tested), `Views/`,
  `Themes/Tokens.axaml` (all colors and icons, light and dark), `Themes/Styles.axaml`, `Localization/Strings*.resx`.
- `tests/*`: one test project per source project. The gRPC codec is cross-checked against Google's generated code.

## Rules

- Warnings are errors; .NET analyzers are at `latest-recommended`. Suppress a rule only with `SuppressMessage` and a justification.
- Package versions live only in `Directory.Packages.props`. Lock files are committed, and CI restores in locked mode.
  Target runtimes are listed in `src/Directory.Build.props`, so publishing with `-r <rid>` does not change lock files.
  `nuget.config` restores only from nuget.org.
- Every UI string goes in both `Strings.resx` (EN) and `Strings.tr.resx` (TR). A test checks parity and that every used key exists.
  Use `{l:Tr Key}` in XAML and `Loc.Get` / `Loc.Format` in code. Engine projects (`Http`, `Grpc`, `Storage`) have their own
  `Messages.resx` / `Messages.tr.resx` and an internal `Messages.Get/Format`; their tests pin en-US.
- Colors only via `{DynamicResource ...}` tokens, never hard-coded, so the theme switches live.
- No element-name or `$parent` bindings inside item templates: put commands on the item view model
  (see `KeyValueRowViewModel.RemoveCommand`). The screen tests fail on binding errors.
- User-input problems are returned as result objects with clear messages, not exceptions.
- Headless UI tests: dispatch a `Func<Task<T>>` onto `HeadlessApp.Session`. Never block with `.GetResult()` on the UI thread.
- Conventional commits (`feat(desktop): ...`). Update `CHANGELOG.md` and the docs with user-facing changes.

## Open work (see docs/roadmap.md)

M1 leftovers:
- Idle memory is ~263 MB against the 200 MB target (NFR-2); see docs/performance.md. Start-up meets NFR-1.
- The release workflow and the macOS/Linux packages have not run on CI yet; only win-x64 was packaged locally.

M2:
- Postman Collection v2.1 and environment import, OpenAPI import.
- cURL import/export, code snippets.
- OAuth 2.0.
