# Changelog

All notable changes to ChronoStroke are recorded here, newest first.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

The section for a version is lifted verbatim into its GitHub release notes by
`.github/workflows/release.yml`, so **a tag with no matching section here fails the release build**.
Add the section before tagging.

## [Unreleased]

## [1.2.0] - 2026-08-21

### Added

- **The interval box has up and down arrows.** Holding one keeps stepping. The Up and Down keys
  do the same thing while the box has focus, and an arrow greys out once the interval reaches
  the 50 ms floor or the 60,000 ms ceiling.
- **A Step box beside the interval sets how far each press moves it** — set it to 5 and 300 ms
  becomes 305 ms. It is saved with the rest of the settings, and a `settings.json` written before
  this existed loads with the 10 ms default rather than the 1 ms minimum.

### Changed

- **The repository is a single branch again.** `develop` is retired: work now happens on a
  short-lived branch that merges into `main` with `--no-ff` and is deleted, and releases are
  tagged directly on `main` rather than through a release branch. Dependabot and the build
  workflow follow `main` accordingly.

## [1.1.0] - 2026-08-20

Everything in this release came out of a full architecture and code quality review of the
repository. Three of the fixes below are things you could hit in normal use.

### Fixed

- **Closing the app no longer kills it with an error.** Closing while the loop was stopped called
  `Close()` from inside the close it was already handling, which WPF rejects. The process then died
  to the unhandled exception instead of exiting — indistinguishable from a normal close unless you
  were watching, and the reason the app never reported it.
- **Num Lock sends Num Lock.** It was treated as an extended key on the basis of a scan-code
  collision with Pause that does not actually exist, so it went out as `E0 45` — a sequence a
  Num Lock key never produces. Anything reading raw scan codes saw some other key.
- **The right key is sent on non-QWERTY layouts.** Scan codes were resolved on a thread-pool
  thread, which does not necessarily share the keyboard layout you captured the key on. They are
  resolved once, up front, on the UI thread.
- **A key can no longer be left held down when you close the app.** The global hotkey and the
  window's message hook stayed live while shutdown waited for the loop to unwind, so a hotkey press
  in that window could start the loop again on the way out.
- **`Esc` leaves a capture box.** The boxes swallow every key so that `Tab` and `Space` can be
  captured, which left no way out of them without a mouse.
- **A rejected hotkey no longer leaves a permanent error.** The box rolls back to the last working
  combination, so the complaint now appears once in the status line rather than sitting under a
  combination that works.
- **`settings.json` is no longer read and rewritten on every keystroke.** Typing an interval ran a
  write per character, and re-read the whole file on each incomplete value — and the value it read
  back skipped the range clamp.
- **Releases are stamped with their own version.** Every tagged build reported `1.0.0` regardless of
  the tag, in its file properties and in any crash report.
- The settings file can no longer take the app down when a write fails in an unusual way.

### Added

- Unhandled-exception handling. A failure now reports itself and shuts down through the normal
  teardown, so the loop still releases its key first, instead of killing the process where it stands.
- An application icon, and publisher and copyright metadata in the executable.
- A warning when the hotkey and the repeated key are the same key with different modifiers — legal,
  and works until you happen to hold that modifier mid-run and the loop switches itself off.
- Screen-reader labelling for the three inputs, and status-line announcements.
- `ChronoStroke.Tests`: 41 tests over the interop's flag decisions, interval validation, key naming
  and the settings format.
- Continuous integration on every push and pull request, and Dependabot.

### Changed

- The window sizes to its content instead of being fixed at 530px, which clipped the status line at
  large text-scaling settings with no way to resize.

## [1.0.0] - 2026-08-19

### Added

- Initial release. Repeats a configurable key or combination into whichever window has focus, at a
  configurable interval, toggled with a global hotkey. Scan-code injection through `SendInput`,
  settings persisted between runs, and a Fluent UI that follows the system light/dark theme.

[Unreleased]: https://github.com/albertm360/chronostroke/compare/v1.2.0...HEAD
[1.2.0]: https://github.com/albertm360/chronostroke/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/albertm360/chronostroke/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/albertm360/chronostroke/releases/tag/v1.0.0
