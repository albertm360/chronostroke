# Changelog

All notable changes to ChronoStroke are recorded here, newest first.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

The section for a version is lifted verbatim into its GitHub release notes by
`.github/workflows/release.yml`, so **a tag with no matching section here fails the release build**.
Add the section before tagging.

## [Unreleased]

## [1.5.0] - 2026-08-27

Two things you can notice, from the remainder of the architecture review. Most of the work behind
this release is internal — the view model and the repeat engine were reorganised and are now far
better covered by tests — and none of that changes what the app does.

### Changed

- **Starting ChronoStroke while it is already running now brings the window you have back,
  instead of opening a second one.** Two copies could not both hold the hotkey: the second one
  reported the combination as taken by "another application", which was true and thoroughly
  confusing, since the other application was ChronoStroke. Both could still be started by hand
  from there, leaving two copies typing into whatever had focus with only one of them able to
  stop. If Windows will not let the window come to the front — it refuses when you are busy in
  another window — its taskbar button flashes instead.

### Fixed

- **A hand-edited `settings.json` with a nonsense key code no longer loads a key that cannot be
  sent.** The interval and the step were checked on load and the two key codes were not, while
  the README said every value was re-validated. A key outside the range Windows defines now
  clears that box to "(not set)", leaving Start disabled until you pick one, rather than looking
  configured and doing nothing when you press it. A stray modifier bit is cleared instead of
  dropping the combination, since `Ctrl+F8` with one extra bit set is plainly meant to be
  `Ctrl+F8`.

## [1.4.0] - 2026-08-25

Three safety fixes from a full architecture review of the repository. All three are things you
could hit in normal use, and two of them change what the app will let you do.

### Fixed

- **Stopping the loop in the instant after starting it could close the app** with the unexpected
  error dialog. The cancellation signal was read on the wrong thread, late enough that a stop
  arriving first left nothing to read. No keystroke was ever left held down by this — the loop
  had not begun — but the app went down with it.
- **Start now refuses to run without a working stop hotkey.** If the hotkey cannot be registered
  — most likely on a first launch where another application already holds `Ctrl+F8` — the loop
  could previously still be started, and the only way to stop it was to reach this window with
  the mouse while the app typed into whatever had focus. Start is now disabled until a hotkey
  registers, with the reason shown above it.
- **A key you need for typing can no longer be made the hotkey on its own.** Windows gives a
  registered hotkey to the app that claimed it rather than to the window you are working in, so
  binding a bare `Space` stopped the space bar working everywhere until ChronoStroke was closed.
  `Enter`, `Tab`, `Esc`, the arrows, `Delete` and ordinary letters and digits behaved the same
  way. These now need a modifier; `F1`–`F24`, `Pause`, `Scroll Lock` and the media keys are
  still accepted on their own, so a bare `F8` remains a valid choice when `Ctrl+F8` is taken.

### Changed

- Every push and pull request now leaves a downloadable build attached to its CI run, so a change
  can be tried out without publishing a release to everyone. Releases can also be started from
  the Actions tab, which checks the changelog and builds before creating the tag rather than
  after it.

## [1.3.0] - 2026-08-24

### Changed

- **The main window follows Fluent UX conventions more closely.** Hotkey and interval/step
  validation messages are now InfoBar-style banners — a tinted, rounded box with an icon — instead
  of bare colored text. Field labels are semibold, the interval/step boxes carry a range hint, and
  the status line gets a divider above it and a colored dot that shows at a glance whether the
  loop is running.
- Text that used to dim itself with a flat opacity now uses the theme's own secondary/tertiary
  text brushes, so light and dark mode each get the alpha the Fluent theme actually intends
  instead of an approximation of it.

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

[Unreleased]: https://github.com/albertm360/chronostroke/compare/v1.4.0...HEAD
[1.4.0]: https://github.com/albertm360/chronostroke/compare/v1.3.0...v1.4.0
[1.3.0]: https://github.com/albertm360/chronostroke/compare/v1.2.0...v1.3.0
[1.2.0]: https://github.com/albertm360/chronostroke/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/albertm360/chronostroke/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/albertm360/chronostroke/releases/tag/v1.0.0
