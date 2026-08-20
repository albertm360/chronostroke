> **Historical.** This is the original plan, kept for the record. Everything below describes
> intent before the app was written, and the "open questions" at the end were all resolved during
> implementation: settings live in `%AppData%\ChronoStroke\settings.json`, the app is a normal
> window rather than a tray utility, and a hotkey that collides with the repeated key is blocked.
> For how ChronoStroke actually works, see the [README](../README.md).

---

# Keystroke Repeater — Spec & Build Plan

## What the app does
A configurable keystroke repeater:
- User picks a key or key combination (e.g. "X", "Ctrl+Shift+E") by pressing it into a capture field
- User sets a repeat interval in milliseconds
- User sets a global hotkey to start/stop the loop
- While running, the app sends the configured keystroke at that interval to whichever window
  currently has focus — game, browser, Explorer, anything
- Settings persist between runs

Primary use case: sending a repeated key to a game window (e.g. "X" in Sea of Thieves) while
the game has focus.

## Requirements
- Interval guard rails — don't allow a value low enough to lock up the machine. Enforce a
  sensible floor and validate input.
- Start/stop must work via the global hotkey while another window has focus.
- Clean shutdown: hotkeys unregistered, timers stopped, no leaked native handles.

## Process
1. **Plan first.** Produce a written plan covering:
   - Project structure and class/file breakdown
   - The full P/Invoke surface needed
   - How the timer and hotkey lifecycles interact
   - How settings are persisted (and where)
   Flag any design decisions you're unsure about and ask me before proceeding.
2. **Wait for my approval** on the plan before writing code.
3. **Implement incrementally**, in this order:
   1. Project scaffold with the Fluent theme rendering correctly
   2. P/Invoke layer with a single hardcoded test keystroke
   3. Hotkey capture UI (recording which key/combo to send)
   4. Timer loop
   5. Global start/stop hotkey
   6. Settings persistence
   7. Publish configuration
   Build and verify compilation after each step. Pause after each so I can test and report back.

## Open questions to resolve during planning
- Where should settings live — `%AppData%`, or next to the exe for portability?
- Should the app minimize to tray, or stay a normal window?
- Should the send-key and the start/stop hotkey be prevented from colliding?