# Copilot Cloud Agent Instructions — nf-debugger

## Repository Overview

This repository contains the **.NET nanoFramework Debugger Library** (`nanoFramework.Tools.Debugger.Net`), a C# .NET library that provides debugging and communication capabilities with [.NET nanoFramework](https://www.nanoframework.net/) devices over USB (Serial) or TCP/IP connections.

It is published as a NuGet package and used by tools such as the nanoFramework VS extension and `nanoff` (nanoFramework firmware flasher).

---

## Project Structure

```
nf-debugger/
├── nanoFramework.Tools.Debugger.sln          # Solution file (Visual Studio 2022)
├── version.json                               # Nerdbank.GitVersioning config (version 2.5.x)
├── azure-pipelines.yml                        # CI build (Azure DevOps, NOT GitHub Actions)
├── azure-pipelines/
│   └── update-dependents.ps1                 # Script to update downstream NuGet consumers
├── nanoFramework.Tools.DebugLibrary.Net/      # Main library project (.csproj)
│   └── nanoFramework.Tools.DebugLibrary.Net.csproj  # Targets net8.0 and net472
├── nanoFramework.Tools.DebugLibrary.Shared/   # Shared project (.shproj) — most source code lives here
│   ├── WireProtocol/                          # Wire protocol engine, commands, packets, controllers
│   ├── NFDevice/                              # NanoDevice, NanoDeviceBase, device access
│   ├── DeviceConfiguration/                  # Device configuration (network, wireless, X.509, etc.)
│   ├── PortSerial/                            # Serial/USB port manager and watcher
│   ├── PortTcpIp/                             # TCP/IP port manager and watcher
│   ├── PortComposite/                         # Composite device manager (serial + TCP/IP)
│   ├── PortDefinitions/                       # IPort, PortBase, DeviceWatcherStatus
│   ├── Capabilities/                          # Target-specific capabilities (ESP32, STM32, NXP, TI)
│   ├── Extensions/                            # Extension methods (cancellation, debugger, device config, etc.)
│   ├── MFDeployTool/                          # Assembly info, device info interfaces and implementations
│   ├── NetworkInformation/                    # Network-related data classes
│   ├── SupportedUSBDevices/                   # USB device descriptors (e.g. STM Discovery)
│   ├── Runtime/                               # Runtime-related classes
│   └── Exceptions/                            # Custom exception types
├── USB Test App WPF/                          # WPF test application (Windows only, not part of the NuGet package)
│   └── Serial Test App WPF.csproj
└── assets/                                    # Logo and other assets
```

The **Shared project** (`nanoFramework.Tools.DebugLibrary.Shared.shproj`) contains the majority of the code and is referenced by the `.Net` project. This is a standard Visual Studio Shared Project pattern — it has no independent output.

---

## Building the Project

### Prerequisites

- .NET SDK 8.0+
- Full git history is **required** (Nerdbank.GitVersioning computes version from commit height)

### Known Build Issue: Shallow Clone

The project uses **Nerdbank.GitVersioning** which requires full git history. Building from a shallow clone will fail with:

```
error MSB4018: Shallow clone lacks the objects required to calculate version height.
```

**Workaround:** Always unshallow the clone before building:

```bash
git fetch --unshallow origin
```

### Build Commands

```bash
# Restore NuGet packages
dotnet restore nanoFramework.Tools.Debugger.sln

# Build the library
dotnet build nanoFramework.Tools.DebugLibrary.Net/nanoFramework.Tools.DebugLibrary.Net.csproj

# Build and pack (creates .nupkg)
dotnet build nanoFramework.Tools.DebugLibrary.Net/nanoFramework.Tools.DebugLibrary.Net.csproj -t:build,pack -p:PublicRelease=true -c Release
```

### Tests

**There are currently no automated tests.** The CI pipeline has a test step that is commented out. The `USB Test App WPF` project serves as a manual test application (Windows only).

---

## CI/CD

- **CI system:** Azure DevOps (`azure-pipelines.yml`), **not** GitHub Actions
- **Build agent:** `windows-latest` (VSBuild is used for the WPF test app too)
- **NuGet publishing:** Azure Artifacts + NuGet.org
- **Code analysis:** SonarCloud
- **Versioning:** Nerdbank.GitVersioning (version `2.5.x`, configured in `version.json`)
- **Package signing:** Azure Key Vault via `sign` CLI tool

There is no `copilot-setup-steps.yml` as CI runs on Azure DevOps, not GitHub Actions.

---

## Key Dependencies and Version Constraints

> **Important version constraints** (do not upgrade without careful analysis):

| Package | Pinned version | Reason |
|---|---|---|
| `Polly` | ≤ 7.2.4 | Dependency conflict with `System.Threading.Tasks.Extensions` |
| `System.Threading.Tasks.Extensions` | ≤ 4.5.4 | VS2019 extension compatibility (transitive dependency conflict with `System.Collections.Immutable`) |
| `Fody` | 6.9.1 | IL weaving for `PropertyChanged.Fody` |
| `PropertyChanged.Fody` | 4.1.0 | Auto `INotifyPropertyChanged` implementation |
| `Nerdbank.GitVersioning` | 3.6.146 | Git-based versioning |
| `System.IO.Ports` | 8.0.0 | Serial port access |
| `NuGet.Build.Tasks.Pack` | 6.12.1 | NuGet pack during build |
| `Microsoft.SourceLink.GitHub` | 8.0.0 | Source linking for debugging |

---

## Code Style and Conventions

- **Language:** C# with `LangVersion: latest`
- **Line endings:** CRLF (enforced by `.editorconfig`)
- **Charset:** UTF-8 with BOM (`utf-8-bom`)
- **Namespace root:** `nanoFramework.Tools.Debugger`
- **License header:** MIT license header on all source files
- **Assembly signing:** Strong-name signed with `key.snk`
- **XML docs:** Generated for all public APIs (`GenerateDocumentationFile=true`)
- **Unsafe code:** Allowed (`AllowUnsafeBlocks=true`)
- **Auto-binding redirects:** Enabled for net472

License header format used throughout the codebase:
```csharp
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
```

Or occasionally:
```csharp
//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//
```

---

## Architecture Overview

### Wire Protocol

The core of the library is the **Wire Protocol** (`nanoFramework.Tools.Debugger.WireProtocol` namespace). It implements the binary protocol used to communicate with nanoFramework firmware running on devices.

Key classes:
- `Engine` — main debugger engine; handles connecting, commanding, and monitoring devices
- `Controller` / `ControllerBase` — manages outgoing/incoming message flow
- `Commands` — all wire protocol command definitions (enums, request/reply structures)
- `IncomingMessage` / `OutgoingMessage` — message wrappers
- `MessageReassembler` — reassembles fragmented incoming packets
- `WireProtocolRequestsStore` — tracks in-flight requests

### Transport Layer

Three transport types are supported, each with a manager (for device discovery/watching) and a port (for communication):

| Transport | Manager class | Port class |
|---|---|---|
| Serial (USB) | `PortSerialManager` | `PortSerial` |
| TCP/IP | `PortTcpIpManager` | `PortTcpIp` |
| Composite | `PortCompositeDeviceManager` | — |

Valid serial baud rates: `921600`, `460800`, `115200` (tried in order).

### Device Abstraction

- `NanoDeviceBase` — base class for all nanoFramework devices
- `NanoDevice<T>` — generic device with typed transport info
- `NanoSerialDevice` — serial-specific device info
- `INanoDevice` — public device interface

### Device Configuration

The `DeviceConfiguration` namespace contains classes for reading/writing device configuration over the wire protocol, including:
- Network configuration (IPv4/IPv6, DHCP, MAC)
- Wireless 802.11 (station and access point modes)
- X.509 certificates (CA root bundles and device certificates)

---

## Important Notes for the Agent

1. **No GitHub Actions CI** — Do not look for or create GitHub Actions workflows for building/testing. CI is Azure DevOps only.

2. **Shallow clone will break the build** — Always run `git fetch --unshallow origin` before attempting to build if the repository was cloned shallowly.

3. **No test suite** — There are no automated tests. Cannot run `dotnet test`. Manual testing requires physical nanoFramework hardware.

4. **Shared project pattern** — When adding new source files, they should typically go into `nanoFramework.Tools.DebugLibrary.Shared/` and be included via the `.projitems` file (`nanoFramework.Tools.DebugLibrary.Net.projitems`), not directly into the `.csproj`.

5. **Wire Protocol sync** — Comments in `Commands.cs` note that certain enums must be kept in sync with the native C++ `Debugger.h` in the nanoFramework firmware. When modifying protocol-related code, check upstream firmware definitions.

6. **Version constraints** — Do not upgrade `Polly` beyond 7.2.4 or add dependencies that require `System.Threading.Tasks.Extensions` > 4.5.4 without thorough compatibility analysis. See `README-BEFORE-UPDATE-REFS.txt`.

7. **Assembly strong-naming** — The assembly is strong-name signed. The `key.snk` file must remain present and referenced in the project.

8. **Multi-target** — The library targets both `net8.0` and `net472`. Ensure any new code is compatible with both targets (or conditionally compiled).
