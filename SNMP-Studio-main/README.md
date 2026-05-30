# SNMP Studio v1.0

Enterprise SNMP browser for Windows — WPF / MVVM / .NET 8

---

## Architecture

```
SNMP.Core      — Interfaces, models, enums. No external dependencies.
SNMP.Engine    — All SNMP logic: client, negotiation, MIB parsing, translation, export.
SNMP.App       — WPF UI, ViewModels, DI wiring.
SNMP.Tests     — xUnit unit tests for classifier, translator, MIB repo, negotiation.
```

### Key Components

| Class | Responsibility |
|---|---|
| `SnmpClient` | GET / WALK (GETNEXT + GETBULK) / SET via SharpSnmpLib. Handles v1/v2c/v3. |
| `NegotiationService` | Auto-probes v2c → v1 → v3 across candidate communities. Returns a structured report. |
| `MibRepository` | Loads RFC ASN.1 MIB files, parses OBJECT-TYPE entries, caches by numeric OID. |
| `OidTranslator` | OID → symbolic name + description. Falls back to 80+ built-in standard OIDs. Interprets values (uptime, enums, speeds). |
| `ErrorClassifier` | Maps raw exceptions to categorised, user-friendly error messages. |
| `ExportService` | CSV / JSON export. Diagnostics ZIP bundle (logs + results + sanitised config — no secrets). |
| `LogService` | In-memory log with Info/Warn/Error/Debug levels and event notification. |
| `MainViewModel` | MVVM hub. Walk, GET, SET (write-guarded), negotiate, MIB import, printer quick view. |

---

## Features

- **SNMP v1 / v2c / v3** (MD5, SHA1, SHA256, SHA512 auth; DES, AES128, AES256 priv)
- **Auto-negotiate** — probes the device to find a working version/community
- **MIB Import** — load `.mib` / `.my` / `.txt` MIB files from files or folders
- **OID Translation** — symbolic names + descriptions, 80+ RFC standard OIDs built in
- **Human-readable context** — uptime formatting, status enums, interface speed, supply levels
- **Virtualized results grid** — handles thousands of OIDs smoothly
- **Write Mode** — disabled by default, requires explicit toggle + confirmation
- **SET** — guarded write with OID / type / value, logged without secrets
- **Printer Quick View** — one-click query of common printer MIB fields (toner, status, serial)
- **In-app diagnostics log** — Info/Warn/Error/Debug with clear button
- **Export** — CSV, JSON, or full diagnostics ZIP bundle
- **Cancellation** — cancel any in-progress walk instantly

---

## Quick Start

1. `git clone https://github.com/Common-joeAI/SNMP-Studio`
2. Open `SNMP.sln` in Visual Studio 2022 (v17.8+)
3. Build → Run (`SNMP.App` as startup project)
4. Enter a host IP, click **Auto-Detect** to negotiate version/community
5. Click **Walk** to enumerate the MIB tree

### Running Tests

```
dotnet test SNMP.Tests
```

---

## Dependencies

| Package | Version | License |
|---|---|---|
| Lextm.SharpSnmpLib | 12.5.2 | MIT |
| Microsoft.Extensions.DependencyInjection | 8.0.0 | MIT |
| xunit | 2.7.0 | Apache-2.0 |
| Moq | 4.20.70 | BSD-3 |

---

## License

Proprietary. See `LICENSE.txt`.
