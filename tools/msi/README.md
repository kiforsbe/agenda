# Native MSI tooling

This directory is self-contained. Copy it to another repository or run it from
any working directory. Windows and PowerShell 7.4 or later are required; Windows supplies
Windows Installer Automation, MakeCab and Expand. No .NET SDK, WinUI, WiX,
third-party PowerShell module or repository checkout is needed by the engine.

```powershell
./package.ps1 -Config ./example.json -PayloadDirectory C:/build/payload `
  -Version 1.2.3 -Architecture x64 -OutputDirectory C:/build/new-installer
./tests/test-config.ps1
./tests/test-msi.ps1
```

The output directory must be new and outside the payload. The command returns
Artifact, Version, Architecture, SHA256 and InspectionReport. It creates an
unsigned MSI and a JSON inspection report. Signing belongs to the caller; the
caller must recompute hashes after signing.

## Configuration version 1

See [example.json](example.json) and [config.schema.json](config.schema.json).
All top-level fields are required. Unknown/duplicate properties are rejected.

| Field | Meaning |
| --- | --- |
| schemaVersion | Integer 1 |
| product.id | Stable identifier used for artifact filenames |
| product.name / manufacturer | Product display name and publisher |
| product.upgradeCode | Permanent product namespace GUID in braces |
| installation.scope | machine |
| installation.directoryName | One owned directory under native Program Files |
| installation.minimumWindowsBuild | Numeric Windows build, at least 22000 |
| application.executable | Payload-relative .exe, including any subdirectory |
| application.icon | Optional payload-relative icon |
| shortcuts | One to ten unique names with location startMenu |
| payload.requiredFiles | Files that must exist in addition to executable/icon |
| payload.allowedExtensions | Approved extensions including the leading dot |
| payload.allowedFiles | Additional exact approved relative paths |

Paths are relative to the payload, independent of the config file's location.
No traversal, rooted paths, alternate streams, wildcard patterns, device names,
trailing dots/spaces, links or reparse points are supported. Approve only the
formats your published application needs. This tool does not classify whether
a permitted file contains secrets. Product configuration owns that policy.

Version is three components, maximum 255.255.65534. Architectures are x64 and
ARM64. The cabinet is limited to 10,000 files and 2 GiB uncompressed content;
a single file must fit a signed 32-bit MSI FileSize. Cabinet directive characters
and short-name collisions are rejected. Use a new output directory on retry.

## Identity and installer behavior

Keep product.upgradeCode and installation.directoryName stable after a release.
Use a different upgradeCode for every independently installed product; do not
reuse example.json's namespace. Component identities depend on that namespace,
architecture and normalized relative path. The engine derives separate x64 and
ARM64 upgrade families; changing architecture does not replace the other family.
Product codes depend on version and package codes are fresh for every authoring.
Do not change a shipped version's contents and expect it to act as an upgrade.

The installer uses native Program Files, an advertised Start-menu shortcut and
standard Windows Installer actions. Close the application before maintenance.
Older MSI versions are removed transactionally, same/newer versions are detected,
and installed products retain MSI maintenance. There is no application launch,
forced shutdown, certificate trust import, custom-action code or user-data
deletion. Uninstall owns only application files and empty app/menu directories.
The engine appends the architecture to installation.directoryName for the app
and menu directories, so separate architecture families cannot overwrite each
other's files. Application-level data and single-instance behavior remain the
consumer's responsibility.

Inspection verifies database identity, file/component mappings, owned folders
and extracted cabinet bytes. Read-only native search/costing preflight runs only
for the host's native architecture, against a copy with new product/upgrade/
package/component identities and disposable target paths. It never initializes
or executes an installation transaction. Cross-architecture inspection checks
tables and bytes but does not launch the foreign installer engine.

Synthetic inspection does not prove actual installation, upgrade, repair,
uninstall, shortcut repair or notification behavior. Perform lifecycle testing
on disposable compatible Windows hosts and run ICE validation before shipping.
Unsigned MSI requires no development certificate import; Windows policy and
reputation can still warn or block it.

The engine does not verify dependency advisories, license compatibility or
application behavior. The consuming repository owns those checks. See
[NOTICE.md](NOTICE.md) and [LICENSE](LICENSE) for provenance and source terms.

## Maintenance and future extraction

| File | Responsibility |
| --- | --- |
| package.ps1 | Public command, filesystem-provider path resolution, output/result contract |
| Configuration.ps1 / config.schema.json | Duplicate-key rejection, strict JSON fields and semantic path checks |
| Identity.ps1 | Product/architecture namespaces and stable UUIDs |
| Payload.ps1 | Link rejection, approved paths, bounded payload enumeration |
| Database.ps1 | Native COM lifetime, parameterized SQL records and tool execution |
| Authoring.ps1 | Cabinet directives, standard tables, short/long aliases and action sequencing |
| Inspection.ps1 | Database mappings, embedded-byte comparison and isolated read-only preflight |
| tests/ | Synthetic config/native fixtures and extraction regression |

The 7.4 floor provides supported PowerShell/.NET APIs for SHA-256 helpers and
the JSON validator; Windows PowerShell 5.1 and early unsupported 7.x releases
are rejected before script execution. No runtime installation is performed.

Native table/action choices are deliberately explicit. Keep RemoveExistingProducts
inside the InstallInitialize/InstallFinalize transaction; preserve all ordinary
unpublish/removal actions required by uninstall. Signature must exist even for a
raw registry AppSearch. Versioned PE files use their fixed binary version; other
files use MsiFileHash records. Root directories and shortcuts use deterministic
8.3|long aliases, as do payload files/subdirectories. Never substitute display
file-version text or introduce executable custom actions as a convenience.

Relative command paths follow the current PowerShell filesystem location, including
after Set-Location. Payload approval and component identities normalize both slash
styles. Paths in JSON remain relative to the payload, not relative to the config.

Before extracting, copy this entire directory including LICENSE/NOTICE and tests.
Run both test scripts in the destination and exercise a second product config
using relative paths from a different directory. Keep publishing, signing, license
review and dependency acquisition in consumer adapters. Do not add imports back
into Agenda or application-specific filenames. A future shared release should be
pinned by its consumers and retain this source license/provenance.
