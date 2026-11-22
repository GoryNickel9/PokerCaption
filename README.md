# PokerCaption

PokerCaption is a Windows desktop utility for arranging poker table windows, displaying an in-session HUD, and tracking session statistics. It also includes optional features that read live table state or interact with the client. This repository contains the desktop application and its native Windows component.

## What the application does

| Component | Behavior |
| --- | --- |
| Table Layout | Detects open table windows and moves or resizes them to configured positions. Layout changes can take effect while the editor is open. |
| HUD | Displays session totals and available table and player data, including public pot information, player actions, and observed action timings. The timing is derived from captured events and can be absent when events are missing. |
| BetScript | Evaluates user-configured formulas using available table values and can prefill the amount field. BetScript leaves the final submission to the player. |
| Mouse wheel and dialog options | Can adjust the amount field, close selected windows, or activate specified dialog buttons when enabled. |

There is no strategy solver in this application. Reviewers should evaluate the HUD, timing data, and input features against the rules for the intended environment before connecting it to a client.

## Process and data boundaries

- **Desktop process.** The C# WinForms application enumerates windows, tracks table placement, and renders its own overlay windows. Client interaction can use Windows control messages, screen coordinates, and synthetic input. Depending on the mode, the application can use `PrintWindow` or sample visible pixels.
- **Native code in the client process.** The normal launch installs Windows message hooks backed by `captionHook.dll`. The native component runs in the target process to inspect runtime UI objects and capture selected UI updates. The x86 build uses the bundled MinHook library. Review the scope of the desktop-wide hook installation separately from the client checks and table selection that determine which windows the application uses.
- **Local IPC.** The desktop and native components exchange commands and observations through a named Windows shared-memory mapping and Windows messages. Review the mapping's permissions, message handling, and trust assumptions alongside the native code.
- **Local files.** Configuration, layout profiles, cumulative session statistics, and diagnostic logs are stored beside the executable. Logs can contain client window titles and sampled table text. Use a test profile when reproducing issues. The application does not require a remote service at runtime.

On the supported rendered client, enabling Table Manager selects a layout-only mode. It keeps the HUD and native table scans available, switches the session sampler to visible pixels, and suspends BetScript, wheel input, and automatic dialog actions. This mode still uses the native component.

## Review and reproduce locally

Build on Windows with the .NET 8 SDK and Zig on `PATH`. The build produces the desktop executable and native DLLs for x86 and x64. Run the preview or tests from the build directory in an interactive Windows session. Those two entry points work without an open poker client.

```powershell
.\build.cmd
Set-Location .\src\PokerCaption\bin\Release\net8.0-windows
.\PokerCaption.exe --ui-preview
.\PokerCaption.exe --selftest2
```

The self-test writes `selftest2.txt` beside the executable. It covers synthetic window placement, HUD rendering and formatting, formula evaluation and the test amount field, and dialog handling with local fixtures. Live-client behavior requires a separate check in an environment authorized for that purpose.

Start the source review at the [desktop entry point](src/PokerCaption/Program.cs), [table service](src/PokerCaption/Engine/TableService.cs), [Windows integration](src/PokerCaption/Native/NativeLayer.cs), and [native component](native/captionHook.c). The [native build script](native/build.ps1) shows which dependencies are linked, including the bundled [MinHook license](native/third_party/minhook/LICENSE.txt).
