# Windows development guide

This guide covers the current **Tauri desktop app** and its shared web editor. Use the one-time setup below, establish a working desktop build, then choose a development loop for the files you are changing. Creating an installer is a separate step.

Checked against repository revision `eefd5ce` on 2026-09-03. Commands and behavior were checked against source, package locks, and the installed Tauri CLI. The desktop launch and watcher behavior were not exercised while writing this guide; the known watcher issues below are based on code inspection.

## Start in the correct folder

The repository root contains `.git`, `DEVELOPER.md`, `Calcpad.Web`, and `Calcpad.Core`. Open **that folder** in VS Code. In a nested checkout such as `CalcpadCE\CalcpadCE`, this is the inner folder. The outer folder will give `fatal: not a git repository` and will not load the repository's VS Code tasks.

**All PowerShell commands in this guide start at the repository root**, unless a block explicitly changes directory. Replace this example path with your checkout:

```powershell
Set-Location -LiteralPath 'C:\path\to\CalcpadCE'
git rev-parse --show-toplevel
git status --short
```

Keep track of any existing changes before building. Some build steps regenerate tracked files.

Jump to: [setup](#one-time-setup) · [desktop baseline](#build-and-run-a-desktop-baseline) · [watch mode](#desktop-watch-mode) · [browser development](#browser-development-with-a-separate-server) · [change checklist](#what-to-rebuild-after-a-change) · [troubleshooting](#troubleshooting) · [packaging](#building-a-distributable).

## How the pieces fit together

| Part | Source location | What it does |
| --- | --- | --- |
| Shared frontend | `Calcpad.Web/frontend/calcpad-frontend/src` | API client, settings, common editor services, and Vue sidebar components. |
| Web editor | `Calcpad.Web/frontend/calcpad-web/src` | Monaco editor, document tabs, app startup, and browser/Tauri adapters. Desktop uses this same UI. |
| Native desktop shell | `Calcpad.Web/frontend/calcpad-desktop/src-tauri` | Rust/Tauri window, native menus, files/dialogs, and server process management. |
| Calculation server | `Calcpad.Web/backend` | ASP.NET Core HTTP API for calculation, rendering, linting, formatting, and exports. |
| Calculation libraries | `Calcpad.Core`, `Calcpad.Highlighter`, `Calcpad.OpenXml` | Calculation engine, language services, and Word output. The server build brings these in through project references. |
| Legacy desktop app | `Calcpad.Wpf` | Separate WPF application. `dotnet build Calcpad.Wpf` builds this app. |
| VS Code extension | `Calcpad.Web/frontend/vscode-calcpad` | Another host for the shared frontend and server; has its own development workflow. |

The current desktop runtime is approximately:

```text
Tauri window / Windows WebView2
  -> calcpad-web UI + calcpad-frontend components
      -> Tauri bridge: native files, dialogs, settings
      -> HTTP API: published Calcpad.Server.exe
          -> Core / Highlighter / OpenXml
```

The **sidecar** is the server process launched beside the desktop app. It is a published copy of the C# code, with its DLLs and runtime files. Editing or building C# source does not automatically replace this copy.

The tools have different jobs:

- **Node/npm** install JavaScript dependencies and run scripts from each folder's `package.json`.
- **TypeScript (`tsc`)** checks/compiles shared TypeScript. **Vite** builds the web UI into `calcpad-web/dist`, or serves it during browser development.
- **.NET SDK** builds, tests, and publishes the C# projects. Publishing assembles a runnable server and its dependencies.
- **Rust/Cargo** compile the native shell. **Tauri CLI** coordinates frontend hooks, Cargo, desktop launch, and packaging.
- **WebView2** displays the desktop UI on Windows.

An npm dependency install, a frontend build, a server publish, and a desktop build are four different operations. None is a substitute for all the others.

## One-time setup

### Install the toolchains

| Requirement | What to use/check |
| --- | --- |
| Git | Available in the terminal. |
| .NET SDK | **10.x SDK**, not only the runtime; projects target `net10.0`. |
| Node.js/npm | Node **22.12+ on the 22.x line, or Node 24.x**. The checked-in Vite 8.1.3 lock requires `^20.19.0 \|\| >=22.12.0`; the older frontend README's Node 18 guidance is insufficient. |
| Rust | Stable **MSVC** toolchain; normally `stable-x86_64-pc-windows-msvc` for x64 Windows. |
| Microsoft C++ Build Tools | Install the **Desktop development with C++** workload and its Windows SDK components. |
| Microsoft Edge WebView2 Runtime | Required for the Tauri window. |
| Python | Needed for rendered-output comparisons/documentation, not the ordinary desktop loop. |

See the official [Tauri Windows prerequisites](https://v2.tauri.app/start/prerequisites/#windows) for C++ Build Tools, WebView2, and Rust setup. Vite's [getting started guide](https://vite.dev/guide/) explains its Node requirements and build/dev roles. Repository package locks remain the version reference for this checkout.

Check the terminal you will actually use:

```powershell
node --version
npm.cmd --version
dotnet --list-sdks
rustc --version
cargo --version
rustup show active-toolchain
Get-Command node, npm.cmd, dotnet, cargo | Select-Object Name, Source
```

Restart VS Code and its terminals after installing or changing toolchains. The `rust-version` in `Cargo.toml` is a declared minimum; use stable Rust for the current locked dependency set.

### Install all three desktop dependency sets

There is no root npm workspace install that covers the desktop. Run these separately; stop if any command fails:

```powershell
npm.cmd --prefix .\Calcpad.Web\frontend\calcpad-frontend ci
if ($LASTEXITCODE -ne 0) { throw 'Shared frontend dependency install failed' }

npm.cmd --prefix .\Calcpad.Web\frontend\calcpad-web ci
if ($LASTEXITCODE -ne 0) { throw 'Web editor dependency install failed' }

npm.cmd --prefix .\Calcpad.Web\frontend\calcpad-desktop ci
if ($LASTEXITCODE -ne 0) { throw 'Desktop dependency install failed' }
```

`npm ci` restores the committed dependency versions and replaces that package's `node_modules`. Use it for a fresh checkout or after a relevant lockfile change, not after every source edit. Use `npm install` when deliberately changing dependencies, and review the resulting manifest/lockfile changes. Initial npm installs, .NET restores, and Cargo builds need network access.

Use `npm.cmd` in PowerShell to avoid selecting the `npm.ps1` shim. The desktop's `local-npm.ps1` additionally puts an optional `calcpad-desktop/.node` directory first on PATH. It **does not download Node**. If that directory is absent, it uses the Node/npm already on PATH. If it exists, check its version when hook behavior differs from your terminal.

## Build and run a desktop baseline

Use this for the first launch, when diagnosing watch problems, or when you want a definite build of your current changes. Save worksheets and close existing CalcpadCE desktop windows first. The single-instance plugin uses the same app identifier for installed and development builds; a second launch can focus the already-running app.

### 1. Publish and stage the server

```powershell
$Desktop = '.\Calcpad.Web\frontend\calcpad-desktop'
powershell -NoProfile -ExecutionPolicy Bypass -File "$Desktop\stage-sidecar.ps1"
if ($LASTEXITCODE -ne 0) { throw 'Server staging failed' }
```

This script determines the Rust host and matching .NET runtime identifier, then calls `vscode-calcpad/scripts/sync-bundled-server.mjs`. That helper publishes a **self-contained Release server** and copies the full publish tree to `src-tauri/binaries`. It is reused tooling; installing/building the VS Code extension is not required for desktop staging.

Look for the helper's `OK` message and resolve earlier errors even if a later message says the publish tree was staged. The PowerShell script does not immediately check the Node helper's exit code, so old files can make its final existence check misleading.

The runtime launches **`Calcpad.Server.exe`**, with sibling DLLs and manifests. Copying only an EXE or only the modified DLL is insufficient. Tauri's Windows resource configuration copies the staged files into the application output; staging also mirrors into existing default `target/debug` and `target/release` folders.

### 2. Build the debug desktop app without an installer

```powershell
$Desktop = '.\Calcpad.Web\frontend\calcpad-desktop'
npm.cmd --prefix $Desktop run build:windows -- --debug --no-bundle --no-sign
if ($LASTEXITCODE -ne 0) { throw 'Desktop debug build failed' }
```

This runs the Windows **before-build** hook to rebuild the shared library and web UI, then compiles the Rust app. The frontend build finishes before Cargo builds the application. It avoids the asynchronous watch startup described below.

`--debug` selects the debug desktop build; `--no-bundle` skips MSI/NSIS packaging; `--no-sign` skips bundle signing. The server from step 1 remains Release-built. Server staging can still sign its apphost if `CALCPAD_SIGN_THUMBPRINT` is configured.

### 3. Run the executable you just built

```powershell
& '.\Calcpad.Web\frontend\calcpad-desktop\src-tauri\target\debug\calcpad-desktop.exe'
```

Open a small worksheet, calculate it, and exercise the feature you changed. Launch this exact file rather than a Start menu shortcut. Keep the output directory together: the server and its dependencies live alongside the desktop EXE.

These paths assume the default Cargo target directory and no explicit `--target`. A build with `--target x86_64-pc-windows-msvc` uses `target/x86_64-pc-windows-msvc/debug` instead. Check the build output if you use `CARGO_TARGET_DIR` or `CARGO_BUILD_TARGET` overrides.

This build does not watch source files. For another UI/Rust edit, close the app and repeat steps 2-3. For a C# or server-resource edit, repeat steps 1-3.

## Desktop watch mode

After dependencies and the server are staged, the intended continuous desktop command is:

```powershell
npm.cmd --prefix .\Calcpad.Web\frontend\calcpad-desktop run dev:windows
```

Use **`dev:windows`**. In this checkout, the plain `dev` npm script explicitly selects the Linux configuration.

The Windows watch chain is:

```text
npm run dev:windows -> tauri dev
  -> build-frontend.ps1 -Watch
       -> remove old calcpad-web/dist
       -> shared npm run build (including settings generation)
       -> shared tsc --watch in a child process
       -> Vite build --watch writes calcpad-web/dist
  -> Tauri's built-in static dev server serves dist and reloads the UI
  -> Cargo builds/runs the native shell and watches Rust changes
  -> shell launches the already-staged C# server
```

There is **no `devUrl` configured**. This uses Tauri's static server, whose default port is 1430, rather than the Vite browser dev server on 5173. UI rebuild/reload can lose temporary editor state; save work before relying on it. C# source is not watched or republished by this command.

### Current watch limitations

The string `beforeDevCommand` starts asynchronously. The hook deletes `dist`, while Tauri CLI 2.11.4 checks whether `frontendDist` exists when deciding to start its built-in server. On a fresh or rebuilding tree, startup can therefore miss the directory. This timing risk is visible in the [hook](../Calcpad.Web/frontend/calcpad-desktop/build-frontend.ps1) and [Tauri CLI source](https://github.com/tauri-apps/tauri/blob/tauri-cli-v2.11.4/crates/tauri-cli/src/dev.rs); it was not reproduced through a GUI test for this document.

The hook also passes the background shared-watcher script path to `Start-Process` without explicit child-command quoting. A checkout path containing spaces can cause that watcher to fail. Check the child PowerShell output rather than assuming every watcher started.

If launch/reload is unreliable, use the [desktop baseline](#build-and-run-a-desktop-baseline) or [browser loop](#browser-development-with-a-separate-server). Reinstalling every dependency does not address these hook issues. These limitations are documented here; the scripts have not been changed by this guide.

Stop with **Ctrl+C in the launching terminal**, and close the app. If a watcher or server remains, identify its checkout and process ID before stopping it. The hook starts a separate TypeScript watcher, so check for an old watcher before starting another session. Do not terminate every Node or .NET process on the machine.

## Browser development with a separate server

This is useful for UI, shared logic, and API changes. It provides Vite's normal fast browser development cycle. Finish with a desktop check for anything involving native menus, files, dialogs, persistence, or the Tauri bridge.

Open two terminals **at the repository root**.

**Terminal 1: run the C# server on a fixed loopback port.**

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project .\Calcpad.Web\backend\Calcpad.Server.csproj -- --urls http://127.0.0.1:9420
```

For automatic C# rebuild/restart during edits, use this instead:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet watch --project .\Calcpad.Web\backend\Calcpad.Server.csproj run -- --urls http://127.0.0.1:9420
```

**Terminal 2: point the UI at that same server and run Vite.**

```powershell
$env:VITE_SERVER_URL = 'http://127.0.0.1:9420'
npm.cmd --prefix .\Calcpad.Web\frontend\calcpad-web run dev
```

Open [the local editor](http://localhost:5173). Vite watches web editor source and aliases `calcpad-frontend` directly to its `src` folder, so normal shared TS/Vue edits participate in this loop. Changes to generated settings defaults still need the full shared build described below.

The explicit server URL matters: an unconfigured backend chooses a free OS-assigned port, while Vite's default proxy targets `localhost:9420`. Starting both with their defaults can show the editor successfully but leave calculation requests disconnected. A `?server=` URL parameter overrides `VITE_SERVER_URL`; remove an old parameter when checking connection problems.

In another terminal, verify server liveness:

```powershell
Invoke-RestMethod 'http://127.0.0.1:9420/api/calcpad/health'
```

Expect `status: ok`. This checks liveness, not calculation correctness. Browser developer tools' Console and Network tabs show JavaScript errors and failing HTTP requests. In Development mode, the standalone server also exposes [Swagger](http://127.0.0.1:9420/swagger).

The desktop's child server has a different lifecycle: it normally chooses its own free port, receives a per-launch API token, and is explicitly run with `ASPNETCORE_ENVIRONMENT=Production`. Even desktop dev mode does not imply Swagger/debug endpoints. A manual unauthenticated request to that child can return 401 while the desktop UI works normally.

## What to rebuild after a change

For a normal development session:

1. Check `git status` and reproduce the issue with a small worksheet or a clear UI action before editing.
2. Find the owning layer in the source map above. Choose the browser loop for shared UI/API work or the desktop baseline for native behavior.
3. Make the change and rebuild the affected layers using the table below. Restage the desktop server whenever its C# code or resources change.
4. Repeat the original example, check nearby behavior, and run the relevant validation commands. Finish native changes in the actual desktop app.
5. Review the diff, including generated settings or rendering stubs, then commit the focused change with a note of what you tested.

| What you changed | Development action | What to verify |
| --- | --- | --- |
| Web editor TS/CSS or shared TS/Vue components | Let Vite/watch rebuild, or repeat desktop baseline steps 2-3. | Actual changed interaction, document contents, and relevant browser/desktop behavior. |
| `DEFAULT_CALCPAD_SETTINGS` or `DEFAULT_PDF_SETTINGS` | Run a full shared `npm run build`; then rebuild/reload the UI. | Generated JSON diff and behavior with the intended saved/default settings. |
| Backend C#, Core, Highlighter, OpenXml | Browser: rerun/watch the server. Desktop: close, stage sidecar, rebuild/relaunch. | Focused C# tests, real API calculation/rendering, and the desktop feature. |
| Backend `template.html`, `Fonts`, `UiAssets`, or server configuration | Rebuild the running backend; restage for desktop. | Fresh rendered output using the changed resource. |
| Rust, native menus, Tauri config/capabilities | Rebuild/relaunch Tauri. Restage only if server inputs also changed. | Native desktop behavior; browser mode cannot validate this. |
| `package.json` / `package-lock.json` | Restore the affected package's dependencies, then restart its build/watch process. | Relevant build plus manifest/lockfile diff. |
| `.cpd` examples or calculation rendering | Run the CLI rendering comparison; regenerate stubs only for intended output changes. | The worksheet, resulting output, and committed stub diff. |
| WPF-only code | Build/run `Calcpad.Wpf`. | The WPF app, separately from the Tauri app. |

### Settings have two extra layers

The shared build's `postbuild` script rewrites `core` and `extras.pdfSettings` in both `src/defaults/settings.default.json` and `dist/defaults/settings.default.json` from TypeScript defaults. `npm run watch` only runs `tsc --watch`; it does not run this generator. Edit the TypeScript source for these generated defaults, then build:

```powershell
npm.cmd --prefix .\Calcpad.Web\frontend\calcpad-frontend run build
git diff -- Calcpad.Web/frontend/calcpad-frontend/src/defaults/settings.default.json
```

Separately, the running app can load saved user settings. Desktop stores active settings under its app-data `settings/active-settings.json`; browser mode uses localStorage. Rebuilding does not clear those settings. Use the app's settings controls or a fresh browser profile when checking a changed default, and preserve your normal settings before resetting them.

## Validation before considering a change finished

Choose checks that cover the changed layer; a successful build is only one part of validation.

**Frontend TypeScript and bundle:**

```powershell
& '.\Calcpad.Web\frontend\calcpad-web\node_modules\.bin\tsc.cmd' -p .\Calcpad.Web\frontend\calcpad-web\tsconfig.json --noEmit
if ($LASTEXITCODE -ne 0) { throw 'Frontend TypeScript check failed' }
npm.cmd --prefix .\Calcpad.Web\frontend\calcpad-web run build
```

Vite's build alone does not typecheck. Plain `tsc` also does not provide full Vue template checking; the shared library explicitly excludes `.vue` files from its compiler input. There is no configured `npm test` script for these desktop/shared/web packages. Exercise the changed UI in the real host.

**C# compilation and tests:**

```powershell
dotnet build .\Calcpad.Web\backend\Calcpad.Server.csproj
if ($LASTEXITCODE -ne 0) { throw 'Backend build failed' }
dotnet test .\Calcpad.Tests\Calcpad.Tests.csproj
```

For a focused test run, add `--filter 'FullyQualifiedName~YourTestClass'` using a real test class. The xUnit project covers Core/Highlighter; it does not exercise the Tauri window or prove that the published server copy is current.

**Rendered-output changes:**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\.github\scripts\compare-renderings.ps1
```

This helper publishes the CLI, creates a temporary Python environment, compares worksheets under `Examples` and `Tests` with committed `.html.stub` files, then removes its temporary `.github/scripts/.venv` and `cli-build` directories. Reserve those directories for the helper. For intentional output changes, rerun with `--write` and inspect the resulting stub diffs. See [DEVELOPER.md](../DEVELOPER.md#ci-automatic-rendered-output-validation) for comparison behavior.

**Before a commit:**

```powershell
git status --short
git diff --stat
git diff --check
```

Review generated settings, manifests, and lockfiles along with your feature diff. Keep unrelated existing work. Do not commit `node_modules`, `dist`, server binaries, Cargo target output, installers, or logs. Existing PR checks emphasize WPF, C# tests, and CLI rendering; a green result does not establish that your changed Tauri/frontend code has been built or exercised.

For an editor change, a useful manual check is: open a representative `.cpd`, perform the exact changed action, verify source preservation and undo, calculate, then save/reopen. Add the relevant preview/export check when output behavior changes.

## Troubleshooting

Start with the **first failing stage** in the terminal, not only the last “build failed” line.

| Symptom | Check / next step |
| --- | --- |
| `not a git repository`, missing `package.json`, missing VS Code tasks | Open the inner repository folder. There is no root npm package for the whole project. |
| `'tauri' is not recognized` | Restore `calcpad-desktop` dependencies. npm scripts use its local Tauri CLI. |
| `'tsc'`, `'tsx'`, or `'vite'` is not recognized | Restore the shared frontend or web package as appropriate. Installing only desktop dependencies is insufficient. |
| Node engine errors or a tool works only outside the hook | Check Node versions on PATH and in the optional desktop `.node` folder. |
| `npm.ps1 cannot be loaded` | Use `npm.cmd`. The examples invoke repository PowerShell scripts with a process-scoped execution-policy argument. |
| `link.exe` missing / native linker failures | Check the C++ desktop workload, Windows SDK, and MSVC Rust toolchain. |
| NETSDK error about unsupported `net10.0` | Check `dotnet --list-sdks` in this terminal and install/select the .NET 10 SDK. |
| Missing `Calcpad.Server.exe`, `hostpolicy.dll`, `.deps.json`, or `.runtimeconfig.json` | Close the app, rerun sidecar staging, and rebuild the desktop output. Check the full publish tree and the earliest publish error. |
| UI edits appear unchanged / a launch only focuses an old window | Close installed/other dev instances; launch the exact build output. Check the Vite build completed and use the baseline if watch startup failed. |
| C# changes appear unchanged | Restage the sidecar. **Server → Restart Server** only restarts the existing published files; it does not compile or publish source. |
| Desktop startup intermittently fails around missing `dist` / index assets | Use the sequential desktop baseline. See the asynchronous watch-hook limitation above. |
| Browser UI works but calculation fails | Match the API URL/port, inspect Network errors, and check `/api/calcpad/health`. An occupied 5173 makes Vite fail because `strictPort` is enabled. |
| Server exits immediately in a scripted launch | Its stdin-EOF watchdog may be stopping it. Use an interactive terminal or the documented `--no-exit-on-stdin-close` / `CALCPAD_DETACHED=1` for an intentionally detached host. |
| Publish/copy reports access denied or files in use | Close the app, stop its dev task, and identify any remaining server/watcher from this checkout before retrying. Also inspect the actual error for registry, filesystem, or managed security-policy restrictions. |
| PDF export fails but calculations work | Check `/api/calcpad/pdf/health` on your standalone server and the app's browser/PDF settings. PDF rendering needs a Chromium-family browser independently of basic server liveness. |
| MSI fails at `light.exe` | This is installer packaging. Use NSIS when you need an EXE; check Tauri's VBSCRIPT prerequisite if MSI is required. |

### Logs and identifying the running build

Use **Server → Show Server Log** in the desktop app. The Settings tab also provides **Open Logs Folder**. Rust and the child server use the app-data `logs` folder; these logs are distinct from frontend build output and browser console errors.

The launch terminal prints a `[sidecar-timing] spawning ...` line with the actual server path. That is useful evidence when you suspect the wrong published copy is running.

To inspect desktop/server processes without stopping them:

```powershell
Get-CimInstance Win32_Process |
    Where-Object { $_.Name -in @('calcpad-desktop.exe', 'CalcpadCE.exe', 'Calcpad.Server.exe') } |
    Select-Object ProcessId, ParentProcessId, Name, ExecutablePath
```

Use executable paths to distinguish checkouts and installed builds. For a bug report, record the Git revision, exact command and working directory, first error, tool versions, running executable path, and whether it reproduces in browser, Tauri, or WPF. Include a small worksheet when calculation behavior is involved.

## Building a distributable

Do this after the development build works. Packaging is unnecessary for ordinary editing and testing.

**Windows NSIS setup EXE:**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Calcpad.Web\frontend\calcpad-desktop\build-desktop.ps1 -Bundles nsis
```

This wrapper republishes the server, builds the frontend and Rust app, and packages NSIS. Without `CALCPAD_SIGN_THUMBPRINT`, it adds `--no-sign` for the Tauri build. Its default without `-Bundles nsis` attempts **both MSI and NSIS**, introducing MSI-specific requirements.

Default x64 installer output: `Calcpad.Web/frontend/calcpad-desktop/src-tauri/target/x86_64-pc-windows-msvc/release/bundle/nsis/`.

**Portable ZIP:**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Calcpad.Web\frontend\calcpad-desktop\build-portable.ps1
```

Default output: `Calcpad.Web/frontend/calcpad-desktop/src-tauri/target/portable/CalcpadCE-portable-win64.zip`. Check that the Tauri build itself succeeded and test the extracted ZIP. The current portable wrapper does not stop immediately on a nonzero native build exit, so an old release folder can otherwise produce a misleading ZIP.

The bare `npm run build:windows` command does **not** stage the server first. Use the wrapper for packaging, or explicitly stage before invoking the bare command. Signing is optional for local development; the repository's signing configuration belongs to packaging and does not need to be set up just to edit code.

## Using the VS Code tasks

Open the repository root and use **Terminal → Run Task** for builds and scripts. Useful labels include `Desktop: Stage Sidecar`, `Desktop: Dev`, `Server: Build (.NET)`, `Web: Dev Server`, `Tests: Run`, and `Scripts: Compare Renderings`.

Use the **Run and Debug** panel for launch configurations: `Wpf: Run`, `Server: Debug (.NET)`, `Desktop: Dev (Tauri)`, and `Extension: Run`.

Be aware of the current definitions:

- `Frontend: Install All Dependencies` installs four npm packages, including the extension; the desktop-only setup above needs three.
- `Build All` builds the extension and web frontend. It does not mean every C# project and the Tauri app have been built.
- `Wpf: Run` launches the legacy app. `Server: Debug (.NET)` launches the separate backend and supports C# breakpoints; point your browser frontend at its port 9420.
- Both the `Desktop: Dev` task and the `Desktop: Dev (Tauri)` launch configuration stage the sidecar, then invoke `npx tauri dev`. Tauri automatically reads the Windows platform config on Windows; it still uses the same watch hook and has the same limitations. The terminal command `dev:windows` makes the selected platform explicit.

For extension development, use the `Extension: Run` launch configuration and consult the [frontend README](../Calcpad.Web/frontend/README.md). For examples, documentation builds, and releases, continue with [DEVELOPER.md](../DEVELOPER.md). Contributor expectations are in [CONTRIBUTING.md](../CONTRIBUTING.md).

The scripts/configuration are the source of truth when commands change: [desktop package](../Calcpad.Web/frontend/calcpad-desktop/package.json), [Windows hooks/resources](../Calcpad.Web/frontend/calcpad-desktop/src-tauri/tauri.windows.conf.json), [server staging](../Calcpad.Web/frontend/calcpad-desktop/stage-sidecar.ps1), [Vite configuration](../Calcpad.Web/frontend/calcpad-web/vite.config.ts), and [VS Code tasks](../.vscode/tasks.json).
