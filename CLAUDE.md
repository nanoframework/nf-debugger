# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Detailed agent instructions already exist at [.github/copilot-instructions.md](.github/copilot-instructions.md) — read that file for the full picture (build issues, dependency version constraints, architecture deep-dive). This file summarizes the essentials and highlights anything that differs.

## What this is

`nanoFramework.Tools.Debugger.Net` — a C# .NET library providing debugging and communication with [.NET nanoFramework](https://www.nanoframework.net/) devices over USB (Serial) or TCP/IP. Published as a NuGet package; consumed by the nanoFramework VS extension and `nanoff` (firmware flasher).

## Build

Prerequisite: .NET SDK 8.0+, and **full git history** — Nerdbank.GitVersioning computes the version from commit height and fails on a shallow clone:

```
error MSB4018: Shallow clone lacks the objects required to calculate version height.
```

If the clone is shallow, unshallow it first:

```bash
git fetch --unshallow origin
```

```bash
# Restore
dotnet restore nanoFramework.Tools.Debugger.sln

# Build the library (targets net8.0 and net472 — keep new code compatible with both)
dotnet build nanoFramework.Tools.DebugLibrary.Net/nanoFramework.Tools.DebugLibrary.Net.csproj

# Build and pack (creates .nupkg)
dotnet build nanoFramework.Tools.DebugLibrary.Net/nanoFramework.Tools.DebugLibrary.Net.csproj -t:build,pack -p:PublicRelease=true -c Release
```

There are **no automated tests** — the CI test step is commented out (`azure-pipelines.yml`, "we don't have tests (yet)"). The `USB Test App WPF/` project is a manual WPF test app (Windows-only, requires physical nanoFramework hardware), not part of the NuGet package.

CI is **Azure DevOps** (`azure-pipelines.yml`), not GitHub Actions — don't look for or add workflows under `.github/workflows` for build/test.

## Architecture

### Shared project pattern

Most source lives in `nanoFramework.Tools.DebugLibrary.Shared/`, a Visual Studio **Shared Project** (`.shproj`, no independent output) referenced by `nanoFramework.Tools.DebugLibrary.Net/` (the actual `.csproj` that targets `net8.0;net472` and produces the NuGet package). New source files go in the Shared project and must be included via `nanoFramework.Tools.DebugLibrary.Net.projitems`, not added directly to the `.csproj`.

### Wire Protocol

The core is the **Wire Protocol** (`nanoFramework.Tools.Debugger.WireProtocol` namespace, under `Shared/WireProtocol/`) — the binary protocol used to talk to nanoFramework firmware:

- `Engine` — main debugger engine: connecting, commanding, monitoring devices
- `Controller` / `ControllerBase` — outgoing/incoming message flow
- `Commands` — all wire protocol command definitions (enums, request/reply structs)
- `IncomingMessage` / `OutgoingMessage` — message wrappers
- `MessageReassembler` — reassembles fragmented incoming packets
- `WireProtocolRequestsStore` — tracks in-flight requests

Many enums/structs in `Commands.cs` (and elsewhere) carry `KEEP IN SYNC WITH native ... in Debugger.h` (or `nanoCLR_Runtime.h`) comments — when touching protocol code, changes must stay aligned with the corresponding native firmware definitions upstream.

### Transport layer

Three transports, each with a manager (discovery/watching) and a port (communication):

| Transport | Manager | Port |
|---|---|---|
| Serial (USB) | `PortSerialManager` | `PortSerial` |
| TCP/IP | `PortTcpIpManager` | `PortTcpIp` |
| Composite (serial + TCP/IP) | `PortCompositeDeviceManager` | — |

Serial baud rates are tried in order: `921600`, `460800`, `115200`.

### Device abstraction

`NanoDeviceBase` (base) → `NanoDevice<T>` (generic, typed transport info) → `NanoSerialDevice` (serial-specific). `INanoDevice` is the public interface.

### Device configuration

`Shared/DeviceConfiguration/` handles reading/writing device config over the wire protocol: network (IPv4/IPv6, DHCP, MAC), wireless 802.11 (station/AP modes), X.509 certificates (CA root bundles, device certs).

## Conventions

- MIT license header on all source files (see existing files for exact wording — two variants are in use).
- CRLF line endings, UTF-8 with BOM, enforced by `.editorconfig`.
- `LangVersion: latest`, unsafe code allowed, XML docs generated for public APIs.
- Assembly is strong-name signed with `nanoFramework.Tools.DebugLibrary.Net/key.snk` — must stay present and referenced.
- Versioning is Nerdbank.GitVersioning-driven (`version.json`); don't hand-edit assembly version numbers.

## Dependency version constraints — do not bump without care

- `Polly` pinned to `7.2.4` — newer versions conflict with `System.Threading.Tasks.Extensions`.
- `System.Threading.Tasks.Extensions` ≤ 4.5.4 — required for VS2019 extension compatibility; see `README-BEFORE-UPDATE-REFS.txt`.
- `Fody` 6.9.1 / `PropertyChanged.Fody` 4.1.0 — IL weaving for auto `INotifyPropertyChanged`.
