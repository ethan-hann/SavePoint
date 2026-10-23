# Testing SavePoint on a Mac and a Linux VM

The Windows machine runs the full suite natively and the Linux suite under WSL. WSL cannot check desktop behavior (notifications, launchers, single instance), and macOS has never run at all (D9). This guide covers both gaps. Run the automated gate first, then the manual checks, then bring the files listed under "What to bring back" to the Windows machine.

## Getting the code there

There is no remote. The whole repository is in one file, `artifacts/savepoint-v2.bundle`, made with `git bundle create artifacts/savepoint-v2.bundle --all`. Copy it to the other machine (USB stick, network share, AirDrop), then:

```bash
git clone savepoint-v2.bundle Savepoint-v2
```

```bash
cd Savepoint-v2
```

On a Mac, the first `git` asks to install the Command Line Tools; accept it. Building also needs the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0 (the macOS Arm64 installer on Apple silicon, x64 on Intel). On Linux, use the distribution's `dotnet-sdk-10.0` package or Microsoft's install script. `bash`, `tar`, and `curl` are already there on both. The bundled git is downloaded and hash-checked by the build, so nothing else is installed.

## 1. The automated gate

```bash
bash build/gate.sh 2>&1 | tee gate-$(uname -s).log
```

It builds with warnings as errors, runs every test, and checks formatting, ending with `gate passed`. Expect about 20 to 30 minutes. Tests that need another OS skip themselves. This is the first time the suite runs on macOS, so failures there are likely and expected; keep the log either way.

## 2. The app from source

```bash
dotnet run --project src/Savepoint.App -c Release
```

On Linux the AppImage can be tried as well: copy `artifacts/release/SavePoint.AppImage`, `chmod +x` it, and run it. Ubuntu 22.04 and later need `libfuse2` for AppImages; without it, `./SavePoint.AppImage --appimage-extract-and-run` works.

## 3. Manual checks on both

- [ ] First run shows where snapshots are stored: `~/Library/Application Support/SavePoint/Storage` on macOS, `~/.local/share/savepoint/storage` on Linux (or under `$XDG_DATA_HOME`).
- [ ] Add a game (a real one if possible, or `tools/FakeGame`, built by the gate), create a profile, play, keep the session, switch between two profiles, and use Go back from History.
- [ ] Notifications appear when a game is ready and when a job finishes.
- [ ] Starting SavePoint a second time brings the first window forward instead of opening another.
- [ ] Turning on the OS's reduce-motion setting (macOS: Accessibility > Display > Reduce motion; GNOME: Settings > Accessibility > Reduce animation) makes progress show a still bar.
- [ ] The tour, F1 help, and every "What's this?" button work, and the text is sharp (Retina or HiDPI).
- [ ] Settings > About > Open the log folder opens the folder in Finder or the file manager.
- [ ] Settings > Move the storage folder to another drive works, and the app finds it after a restart.

## 4. macOS only

- [ ] **Quitting with Cmd+Q** (and SavePoint > Quit in the menu bar) while a snapshot is being taken: the same "a job is running" question appears as when the window is closed. The exit guard hooks the window's close, and Cmd+Q may take a different path on macOS.
- [ ] **Shortcuts:** help opens with Cmd+Shift+/ (and F1 on keyboards that have it). In Activity > Details, Cmd+C copies the details. The code listens for Ctrl+C there, so Cmd+C is expected to fail; this confirms it.
- [ ] **A game that is an `.app` bundle** (any Mac game from Steam, or any app as a stand-in) starts from SavePoint, and SavePoint notices when it quits.
- [ ] **Case:** APFS ignores case by default. Taking and switching snapshots of a folder holding `Mods/Foo` and `Mods/foo` refuses with a clear message instead of losing a file.
- [ ] **Safety:** choosing `/System`, `/Applications`, or `~/Library` itself as a game folder is refused.
- [ ] **VoiceOver** (Cmd+F5): the Games screen, the launch dialog, and Settings read their buttons and rows.

## 5. Linux VM only

- [ ] **A launcher game:** with Steam installed, a game started through Steam (or a Proton game) is followed until it quits.
- [ ] **A game on an NTFS or exFAT drive** (a second virtual disk formatted that way): taking and switching snapshots works, and a case-only clash is refused (the WSL gate could only check this on the 9p mount).
- [ ] **Wayland and X11:** if the VM offers both at login, start SavePoint once under each.
- [ ] **Uninstall:** deleting the AppImage leaves `~/.local/share/savepoint` and `~/.config/savepoint` in place, and a new AppImage picks the games up again.

## What to bring back

Copy these into `F:\GitHub Repos\Savepoint-v2\.tools\field-tests\<mac|linux>\` on the Windows machine:

- `gate-Darwin.log` or `gate-Linux.log` from the clone's folder;
- the newest files from SavePoint's log folder (Settings > About > Open the log folder);
- a short note of each check that failed or felt wrong, with a screenshot where it helps.
