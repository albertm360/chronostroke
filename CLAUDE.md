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

## Build & publish
- Build after every change; confirm it compiles before moving on.
- Ship target: `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`

## Working style
- **Don't over-engineer.** This is a single-window utility, not an enterprise app.
- Explain P/Invoke code as you write it — I want to understand the interop, not just have it.
- Correct cleanup matters: unregister hotkeys and stop timers on exit, no leaked native handles.
- You cannot run a WPF window or observe whether keystrokes land in a game. After each step,
  tell me exactly what to run and what I should expect to see. I'll report back.