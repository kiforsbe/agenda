# Generic MSI packaging and Agenda integration

Date: 2026-10-20
Status: Proposed for written-design approval

## Intent and scope

Add Agenda MSI installers using the native Windows Installer authoring approach
in the sibling Procsight repository. Make the engine reusable immediately through
a JSON configuration file and a repository-independent command interface. The
user selected JSON because PowerShell supports it directly. A future repository
can own the engine without redesigning its interface.

Also adapt relevant build verification and project documentation from Procsight.
Keep Agenda's existing MSIX commands, version source, x64/ARM64 support, native
resource fixes and test entry points working.

## Approach

Keep a self-contained `tools/msi/` directory with the engine, JSON Schema,
example configuration, synthetic tests and usage documentation. Agenda owns
`deployment/msi.json` and invokes the engine from `scripts/package.ps1`.

An Agenda-specific copy would be smaller initially but would impede extraction.
A new installer framework would add dependencies and depart from the requested
Procsight implementation. The recommended approach is a configurable native
engine adapted from Procsight with no new parser or installer-framework package.

The engine requires Windows and PowerShell 7. It uses Windows Installer
Automation, MakeCab and Expand. Optional signing is an Agenda orchestration step
using Windows SDK SignTool; private keys and passwords never enter configuration.

## Engine contract

The public command is `tools/msi/package.ps1` with required `-Config`,
`-PayloadDirectory`, `-Version`, `-Architecture` and `-OutputDirectory` arguments.
Architecture accepts x64 and ARM64. Paths are resolved to absolute paths at entry;
configured executable and asset paths are relative to the payload root.

The command does not restore, compile, publish, inspect a Git repository, install
software, create certificates or change certificate trust. It returns a structured
result containing the artifact path, version, architecture, SHA-256 and inspection
report path. Agenda adds source commit and signing status to its release manifest.
Manifest hashes describe final bytes after any signing.

JSON configuration version 1 contains:

- `schemaVersion`: integer 1.
- `product`: stable `id`, display `name`, `manufacturer` and permanent
  `upgradeCode` GUID.
- `installation`: `scope` (machine in version 1), `directoryName` and
  `minimumWindowsBuild`.
- `application`: payload-relative `executable` and optional `icon`.
- `shortcuts`: named Start-menu shortcuts targeting the configured executable.
- `payload`: explicit required file paths and approved extension/relative-path
  rules, permitting generic applications to include their own resource formats.

The engine ships a schema and uses PowerShell's JSON/schema facilities together
with semantic validation. Reject unknown properties, duplicate keys, invalid
types, unsupported schema versions, malformed GUIDs, invalid MSI versions,
duplicate shortcuts and missing required files before authoring. Paths must stay
within the payload tree; reject traversal, links/reparse points, unsupported
cabinet names and payloads exceeding the bounded cabinet/file limits.

The configuration is data, with no script expressions, hooks or custom actions.
Architecture and version are build arguments rather than duplicated release
metadata. Relative-path matching and collision checks account for Windows's
case-insensitive paths. Diagnostics identify the invalid field or payload file.

## Installer behavior

Install per machine under the native Program Files folder. Create an advertised
Start-menu shortcut and Apps/Settings registration with the configured icon.
Use standard MSI actions; do not automatically start or terminate the app.

Derive stable component identities from the product namespace, architecture and
normalized relative path. Derive architecture-specific upgrade identities from
the configured permanent GUID. Versions change product identity; each newly
authored package receives a fresh package identity. Different products must never
share component or upgrade identities. Configuration documentation identifies
which fields must remain stable for upgrades.

Support transactional removal of older MSI versions and rejection of downgrades
and duplicate same-version installation, while retaining normal installed-product
maintenance. Own only installed files and app/menu directories. Preserve all
per-user settings, calendar feeds, caches and credentials on uninstall.

Author architecture-correct summary metadata, component attributes and OS checks.
Agenda retains its declared Windows build floor of 22000; do not inherit
Procsight's 26100 floor. Validate the supported three-field MSI version range
against assembly-version constraints used by Agenda.

## Agenda integration and build tooling

Extend `scripts/package.ps1` with `-Format Msix|Msi`, defaulting to Msix for command
compatibility. Keep existing MSIX certificate parameters working. MSI defaults
to unsigned output and supports explicitly requested signing. Reject irrelevant
certificate/trust switches rather than silently importing trust or creating a
development certificate for MSI.

Extract shared staging, resource checks, version reading and tool invocation into
Agenda script helpers. Publish to isolated output/intermediate directories so
architecture and packaging builds cannot reuse stale development outputs. Keep
Agenda's notification runtime extraction and compiled XAML/PRI inclusion.
Collect applicable license texts and notices before invoking the MSI engine.
Use versioned, architecture-specific artifact names and preserve previous builds.

Add `scripts/verify.ps1` to run packaging/configuration checks, existing Agenda test
executables, Release builds and whitespace checks with consistent failure
handling. Preserve `build.ps1`, `run.ps1` and `test.ps1` entry points and Agenda's
`AgendaVersion` source. Do not copy Procsight's application-specific SDK/runtime
pins, solution layout, test-runner assumptions or version-property names.

Extend CI to verify and build/upload MSI artifacts for x64 and ARM64, separating
cross-build checks from executable acceptance on compatible hosts. Preserve MSIX
regression coverage. Automated version edits, commits, tags, release publication
and draft-release creation are outside this change.

## Documentation and licensing

Adapt `SECURITY.md`, `CONTRIBUTING.md`, `DEVELOPERS.md`, `CODE_OF_CONDUCT.md`,
`SUPPORT.md`, third-party notices, PR template and feature-request template to
Agenda. Link them from README and correct obsolete licensing/packaging statements.
Describe secret feed URLs, encrypted per-user data, provider authentication,
notifications, distribution formats and actual validation limits. Do not import
Procsight-specific process collection, AI providers or administrator-mode claims.
Do not invent private contact addresses or promised response times.

The previous proposal explicitly offered AGPL-3.0 adoption from Procsight. This
design includes that choice for approval; the JSON-format selection alone is not
treated as a separate explicit license decision. Preserve applicable provenance
and notices when adapting Procsight code. The eventual shared-tool repository's
license is a future owner decision, subject to the rights in reused code.

Before choosing or retaining dependencies for this distribution, check the live
authoritative registry and upstream guidance for latest stable compatible
versions, deprecations/replacements, known advisories and actual package license
and redistribution terms. Include transitive packages, native binaries and
bundled runtimes; record compatible exceptions and unresolved terms. Generate
Agenda's inventory rather than copying Procsight's package list. Advisory scans
alone do not establish security or license compatibility.

## Verification and acceptance

- Exercise valid/invalid JSON and schema inputs, path confinement, duplicate keys,
  namespace isolation, stable identities and architecture/version boundaries.
- Author synthetic MSI databases/cabinets, inspect standard tables and compare
  extracted file bytes with the input payload. Include Procsight's required
  Signature-table regression and removal/component membership checks.
- Run native read-only search/costing preflight against disposable MSI copies with
  fresh product, upgrade, package and component identities. Never initialize an
  installation transaction or open the deliverable identity for execution.
- Build/test Agenda and create/inspect real x64 and ARM64 MSI/MSIX artifacts,
  checking resources, architecture, version, notices and final manifest hashes.
- Check portable startup with a synthetic profile. Verify installed startup,
  reminder delivery/activation, repair, upgrade, downgrade rejection and uninstall
  in a disposable Windows environment when available. Run ICE validation where
  supported. Report unavailable checks explicitly rather than implying acceptance.
- Run the engine with a second synthetic product configuration from outside
  Agenda's repository layout to prove it has no Agenda or WinUI assumptions.
- Check documentation links, examples and `git diff --check`.

Procsight's documented native database/payload tests passed there, but actual
installed lifecycle and ICE acceptance remain outstanding. Those claims are not
transferred to Agenda or to the generic engine.

## Extraction boundary

All reusable engine code and its standalone tests/docs live inside `tools/msi/`.
They must not import files outside that directory. The Agenda adapter and config
stay in Agenda. A later extraction can replace the local directory with a pinned
shared-tool release; no shared repository, download mechanism or auto-update
feature is created in this change.
