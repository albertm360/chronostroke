# Project: Keystroke Repeater

A small Windows 11 desktop utility. Single window, no server component, no cloud.

## Stack
- C# / WPF targeting `net10.0-windows` (.NET 10 SDK, LTS)
- WPF's built-in Fluent theme, following the system light/dark setting
- MVVM structure

## Dependency rules
- **CommunityToolkit.Mvvm is the only NuGet dependency permitted in the app.** Ask before adding
  anything else.
- `ChronoStroke.Tests` is an agreed exception, and the only one: it may use Microsoft.NET.Test.Sdk
  and xUnit. It is never published and nothing it references may leak into the app project.
- Do NOT add WPF-UI, MahApps, ModernWpf, or any other third-party UI package — the built-in
  Fluent theme covers what this app needs.
- Do NOT add InputSimulator, WindowsInput, or similar input-simulation packages. The Win32
  interop is written by hand.
- No DI container, no logging framework, no settings library.

## Input injection
- Use `SendInput` from `user32.dll` via P/Invoke. `SendKeys.Send` is not acceptable — it does
  not work reliably with games.
- Global hotkeys via `RegisterHotKey` / `UnregisterHotKey`.
- Prefer `[LibraryImport]` source-generated interop over `[DllImport]` where it applies.
- Look up current Win32 docs on learn.microsoft.com for `INPUT` / `KEYBDINPUT` struct layouts
  and virtual key codes. Do not write these from memory — a field-size mismatch fails silently
  at runtime rather than at compile time.

## Reference documentation
Consult before writing code; do not rely on memory for Fluent theme setup or Win32 signatures.
- Fluent theme in WPF (most detailed source; covers resource dictionaries and the ThemeMode APIs):
  https://github.com/dotnet/wpf/blob/main/Documentation/docs/using-fluent.md
- What's new in WPF for .NET 9 (Fluent theme intro):
  https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net90
- WPF docs home: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/
- Styles and templates overview:
  https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/styles-templates-overview
- What's new in .NET 10:
  https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview
- C# coding conventions:
  https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions
- Framework design guidelines:
  https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/

## Build, test & release
- Build after every change; confirm it compiles before moving on. Run `dotnet test` as well — it
  takes under a second, and it covers the interop's flag decisions, where a wrong value fails
  silently at runtime rather than at compile time.
- Ship target: `dotnet publish -c Release`. The runtime identifier, self-contained and
  single-file settings all live in the csproj, so the bare command produces the shippable exe.
- Releases are cut from tags, not built by hand. `release.yml` builds the tag, stamps the version
  from it and publishes the GitHub release; `build.yml` validates every push and pull request.
- **Add a `## [x.y.z]` section to `CHANGELOG.md` before tagging.** The release build fails
  without one, deliberately. Tags are `vX.Y.Z`; the workflow rejects any other shape.

## Commits

- **Conventional Commits** for commit subjects and PR titles: `<type>(<optional scope>): <description>`
  — for example `fix(hotkey): require a registered hotkey before the loop can start`.
- The subject takes the format; **the body stays prose.** The commit messages here record reasoning,
  measurements and rejected alternatives, and that is worth more than any convention applied to them.
- **No tool attribution anywhere** — no `Co-Authored-By` trailer, no "generated with" footer, no
  mention of whatever wrote the change, in commit messages, PR bodies or files in the repo.
- History before 2026-08-25 is free-form prose. The convention applies going forward; merged history
  was deliberately left alone rather than rewritten for consistency.

## Branching
- `main` is the only long-lived branch, and the only one that exists on the remote. This was
  gitflow until 1.2.0; `develop` was retired because a solo project got no benefit from a second
  permanent branch that had to be kept in sync with the first one by hand.
- Work happens on a short-lived branch — `feature/…`, or `fix/…` — which merges back into `main`
  with `--no-ff` and is then deleted, locally and on the remote. The merge commit is what keeps
  a feature legible as one unit in the log after its branch is gone.
- Releases are tagged directly on `main`; there is no release branch. Tag the merge commit that
  finishes the release, and push the tag to publish.
- Dependabot is configured in `.github/dependabot.yml` with no `target-branch`, so it aims at the
  default branch, `main`. Dependabot reads that file from the default branch only, so a change to
  it has no effect until it lands there.

## Working style
- **Don't over-engineer.** This is a single-window utility, not an enterprise app.
- Explain P/Invoke code as you write it — I want to understand the interop, not just have it.
- Correct cleanup matters: unregister hotkeys and stop timers on exit, no leaked native handles.
- You can see the window, so check it yourself before asking me. Launch the built exe, wait for
  its `MainWindowHandle`, then `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT)` into a bitmap and
  read the PNG; the exit code on the way out proves the XAML parsed and that shutdown is clean.
  Layout, alignment, theme brushes, disabled and error states are all settled that way. Close
  with `WM_CLOSE` rather than killing the process, so the app runs its real shutdown — and if a
  build is blocked by a locked exe, that is usually an instance you left running. Two traps:
  capture with `PrintWindow`, never `CopyFromScreen`, which grabs whatever is on my desktop
  instead of the window; and the capturing process must call `SetProcessDpiAwarenessContext`
  first or the window rect comes back virtualised on a scaled monitor.
- What a still frame cannot show is still mine: hover and pressed feedback, animation, how a
  hold-to-repeat feels, and whether keystrokes reach a game. For those, and for anything
  involving a real target application, tell me exactly what to run and what I should expect to
  see — I'll report back.
- Verify whatever else you can unaided too. `dotnet test` covers the pure logic. A throwaway
  console project in the scratchpad, linking this app's own source files, will answer a Win32
  question outright — that is how the Num Lock scan code was settled, rather than by reasoning
  about what `MapVirtualKeyW` probably returns.