# Generic MSI Packaging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a standalone JSON-configured MSI engine, integrate it into Agenda, and adapt the relevant build and community documentation from Procsight.

**Architecture:** A self-contained `tools/msi/` directory accepts an already published payload and authors/inspects an MSI using native Windows tools. Agenda owns publishing, dependency review, license collection, signing and release provenance. The extraction boundary must work for a second product without Agenda or WinUI assumptions.

**Tech Stack:** Windows, PowerShell 7, JSON/JSON Schema, Windows Installer Automation, MakeCab/Expand, existing .NET 10 and Windows SDK tooling. No YAML parser, WiX or new installer-framework dependency.

**Spec:** [Approved design](../specs/2026-10-20-generic-msi-packaging-design.md)

## Global Constraints

- Architectures: x64 and ARM64; Agenda minimum Windows build: 22000.
- Keep `AgendaVersion` in `Directory.Build.props` as the product version source.
- Preserve `scripts/build.ps1`, `scripts/run.ps1`, `scripts/test.ps1` and existing MSIX certificate parameters; `-Format Msix` remains the packaging default.
- Generic engine imports nothing outside `tools/msi/`; no application names, Git, .NET or WinUI assumptions in engine code.
- No product installation, certificate trust changes, automatic application launch/termination, executable custom actions or user-data removal.
- Live registry/upstream/version/advisory/license review includes transitive packages and bundled native/runtime files. Record all unresolved findings and compatibility exceptions; do not treat scans as complete security or license assurance.
- AGPL-3.0 adoption is included in the approved scope; preserve Procsight provenance and applicable notices. Do not promise a future permissive license for code ported under AGPL.
- No release commits/tags/version-bump automation or publication workflow; implementation commits are separate from release preparation.
- Installed lifecycle and ICE checks require explicit evidence; unavailable environments must be reported.

## Review Focus

1. Two products or architectures with identical file names must not share upgrade/component identities: assert isolation in Task 3.
2. Duplicate JSON keys, traversal and links must fail before MSI authoring, including paths with spaces and mixed separators: assert in Tasks 2 and 3.
3. Failed or repeated builds must preserve previous artifacts and development intermediates: assert in Tasks 3 and 4.
4. A signing failure must not produce metadata claiming a signed deliverable or a hash from pre-signing bytes: assert in Task 4.
5. Real ARM64 payloads and unpackaged notification registration need validation beyond x64 synthetic checks: verify architecture tables in Task 3 and record actual-host acceptance in Task 6.

## File responsibilities

| Path | Responsibility |
| --- | --- |
| `tools/msi/package.ps1` | Standalone public command and structured result |
| `tools/msi/Configuration.ps1`, `config.schema.json` | Strict JSON/schema and semantic validation |
| `tools/msi/Identity.ps1` | Stable scoped MSI identifiers and version checks |
| `tools/msi/Payload.ps1` | Confined payload enumeration and approval rules |
| `tools/msi/Database.ps1` | COM lifetime, SQL rows and native tool invocation |
| `tools/msi/Authoring.ps1` | Native cabinet, database and standard actions |
| `tools/msi/Inspection.ps1` | Database, extraction/hash and isolated read-only preflight |
| `tools/msi/tests/*.ps1`, `example.json`, `README.md`, `NOTICE.md` | Portable tests, usage and provenance |
| `deployment/msi.json` | Agenda product metadata and payload policy |
| `scripts/packaging/Common.ps1`, `Msix.ps1`, `Signing.ps1` | Agenda publishing/resource/license, MSIX and signing helpers |
| `scripts/dependencies/Common.ps1`, `test-dependencies.ps1` | Dependency evidence, fail-closed advisory result handling |
| `scripts/test-packaging.ps1`, `verify.ps1` | Adapter regression checks and standard verification |
| `docs/DEPENDENCIES.md`, `THIRD_PARTY_NOTICES.md` | Actual Agenda dependency review and redistribution inventory |
| Root community documents and GitHub templates | Agenda-specific maintainer/contributor guidance |

### Task 1: Establish the distribution dependency and license baseline

**Files:** Create `docs/DEPENDENCIES.md`, `scripts/dependencies/Common.ps1`, `scripts/test-dependencies.ps1`; modify relevant `src/**/*.csproj` and `Directory.Build.props` only when the review justifies it; create resolved `packages.lock.json` files if locked restore is introduced.

**Interfaces:** `Get-AgendaDependencyReport -Project <path> -Kind outdated|deprecated|vulnerable` returns parsed registry-backed reports including transitives. `Assert-AgendaVulnerabilityReport -Report <object>` rejects every advisory severity and failed/incomplete retrieval. `Get-AgendaDependencyProjects` returns all six existing app/library/test project paths.

- [ ] Record current toolchain, resolved dependency graphs and runtime packs for both RIDs. Query live NuGet registration/index metadata, upstream guidance and actual package terms for all resolved components; review GitHub Actions upstream releases/terms before selecting their pins.
- [ ] Write synthetic report tests: advisory => rejection, failed retrieval => rejection, complete empty advisory report => acceptance; report tests must not claim to establish license compatibility.
- [ ] Run `pwsh -NoProfile -File scripts/test-dependencies.ps1`; confirm failure before implementation.
- [ ] Implement report helpers and select a coherent stable compatible dependency set. Document old/new versions, deprecations, replacements, advisories, actual license files/redistribution conditions and explicit retained-version exceptions. Block distribution claims where terms remain unresolved.
- [ ] Run the helper checks, `./scripts/test.ps1`, and `./scripts/build.ps1 -Configuration Release -Architecture x64` after any dependency edits. Check ARM64 restore/build for changed native packages. Record actual commands and results.
- [ ] Commit only dependency review/tooling and justified dependency changes.

### Task 2: Define and validate the standalone configuration contract

**Files:** Create `tools/msi/Configuration.ps1`, `Identity.ps1`, `Payload.ps1`, `config.schema.json`, `example.json`, `tests/test-config.ps1`, `NOTICE.md`.

**Interfaces:** `Read-MsiConfiguration -Path <string>` returns validated configuration. `Assert-MsiVersion -Version <string>` accepts three decimal components with major/minor 0..255 and patch 0..65534 (assembly-compatible). `Get-MsiGuid -Namespace <guid> -Scope <string>` returns stable GUIDs. `Get-MsiPayload -Configuration <object> -Folder <string>` returns sorted objects with `Id`, `Component`, `Relative`, `FullName`, `Name`, `Length`.

- [ ] Write failing config tests for schema version 1, required nested fields, unknown properties, duplicate keys, wrong types, malformed GUIDs, forbidden device/path names, collisions, unsupported scope and unknown shortcut locations. Assert that valid JSON loads without external modules.
- [ ] Add payload cases for empty/missing files, leading/rooted/drive-relative/traversing paths, alternate data streams, links/reparse points, spaces, mixed separators and case collisions. A link test must report an unavailable privilege rather than falsely pass.
- [ ] Run `pwsh -NoProfile -File tools/msi/tests/test-config.ps1`; confirm expected failures.
- [ ] Implement schema/semantic validation. Check duplicate properties with the built-in .NET JSON document reader before conversion. Permit only `schemaVersion`, `product`, `installation`, `application`, `shortcuts`, `payload`, with the exact fields from the spec. Define `payload.requiredFiles`, `allowedExtensions`, `allowedFiles`; approval requires extension or exact-path membership. Reject links including a linked payload root.
- [ ] Implement namespace-derived identities and confined enumeration. Enforce a single cabinet, at most 10,000 files and less than/equal to 2 GiB payload; reject invalid cabinet directive characters. Normalize separators and case for identity/collision checks.
- [ ] Re-run config checks, verify the example parses, and commit the configuration contract with provenance.

### Task 3: Port native MSI authoring and inspection behind the generic interface

**Files:** Create `tools/msi/Database.ps1`, `Authoring.ps1`, `Inspection.ps1`, `package.ps1`, `tests/test-msi.ps1`, `README.md`.

**Interfaces:** `New-MsiPackage -Configuration <object> -Folder <string> -Path <string> -Version <string> -Architecture x64|ARM64` authors the file. `Read-MsiPackage -Path <string>` returns inspected tables. `Assert-MsiContents -Path <string> -Folder <string> -Configuration <object> -Version <string> -Architecture <string> -InspectionDirectory <string>` verifies tables/bytes. `Assert-MsiPreflight -Path <string>` uses an isolated copy. The public command accepts the five required parameters in the spec and returns `{Artifact, Version, Architecture, SHA256, InspectionReport}`.

- [ ] Port Procsight's synthetic fixture assertions first; change fixtures to two arbitrary product names, both architectures and product versions 0.1.0/0.2.0. Run and capture failures before authoring exists.
- [ ] Adapt native COM/database helpers and authoring. Use configuration exclusively for product/executable/icon/shortcut/OS values. Set summary templates `x64;1033` or `Arm64;1033`, native 64-bit component attributes, architecture-specific upgrade/component identity and a new package code per authoring run.
- [ ] Preserve the Signature/AppSearch regression fix, MsiFileHash entries, feature membership, owned-folder removal, upgrade/downgrade rows and standard action order. Include no CustomAction table or registry-writing custom action. Retain installed-product maintenance through `Installed` conditions.
- [ ] Port extraction and byte comparison, plus preflight copies with fresh product/upgrade/package/component GUIDs. Limit native calls to search, costing and InstallValidate; confirm original bytes and product registration state remain unchanged.
- [ ] Test stable same-product identities, different product/architecture isolation, version-change product codes, cabinet/table consistency, missing required tables and rejected custom-action tables. Repeated output paths must fail without overwriting earlier files; cleanup resolves and confines every recursive target.
- [ ] Run `pwsh -NoProfile -File tools/msi/tests/test-msi.ps1`. Copy only `tools/msi/` to a temporary path containing spaces outside Agenda and invoke it on the second fixture product. Assert no Agenda or WinUI dependencies are needed.
- [ ] Document exact config/CLI fields, stable identity rules, constraints and inspection limits; commit the standalone engine.

### Task 4: Integrate Agenda publishing, notices, signing and both formats

**Files:** Create `deployment/msi.json`, `scripts/packaging/Common.ps1`, `Msix.ps1`, `Signing.ps1`, `scripts/test-packaging.ps1`; modify `scripts/package.ps1`, `Directory.Build.props`, `src/Agenda.App/Agenda.App.csproj` only for isolated publishing as needed.

**Interfaces:** `Get-AgendaVersion -Root <string>` reads `AgendaVersion`. `Publish-AgendaPayload -Architecture <string> -BuildRoot <string>` returns the isolated payload folder. `Assert-AgendaPayload -Folder <string>` checks app, PRI, XBF, notification runtime and assets. `Export-AgendaLicenseMaterials -AssetsPath <string> -Destination <string>` includes package and resolved runtime-pack license/notice materials. `Write-AgendaArtifactManifest -Artifact <string> -Version <string> -Commit <string> -Format <string> -Architecture <string> -Signed <bool>` hashes final bytes.

- [ ] Write adapter regression tests for default Msix selection, both architectures, original signing parameter compatibility, rejection of MSI certificate-store/trust options, and missing XAML/notification resources. Add tests proving a failed signing operation cannot emit a success manifest and final hashes are computed after signing.
- [ ] Run `pwsh -NoProfile -File scripts/test-packaging.ps1`; confirm failures before changing the adapter.
- [ ] Extract existing MSIX/certificate behavior into focused helpers. Keep default MSIX compatibility output paths, and retain every versioned build in its unique directory. Restore previous process environment values on script exit.
- [ ] Add `-Format Msix|Msi` with default Msix. Publish using unique output and per-project intermediate roots, exclude original bin/obj globs, preserve Agenda's XAML and notification targets. Fail on missing resources before authoring either format.
- [ ] Collect applicable source notices and actual resolved direct/transitive/native/runtime license files; detect missing required redistribution materials. Include `Agenda-LICENSE.txt`, `Agenda-THIRD-PARTY-NOTICES.md` and `Licenses/` in payload policy. Call the generic engine with build arguments and `deployment/msi.json`.
- [ ] For MSI, remain unsigned unless an explicit certificate path is supplied. Add optional HTTPS timestamp input; validate explicit signing and record signed status only after signer verification. Do not create/import a development certificate for MSI.
- [ ] Run adapter tests and create real x64 MSI and MSIX artifacts. Compare inspected file bytes and final artifact manifests, verify package version, and confirm previous builds/development outputs remain intact. Commit integration changes.

### Task 5: Adopt Agenda documentation and contributor metadata

**Files:** Create `LICENSE`, `THIRD_PARTY_NOTICES.md`, `SECURITY.md`, `CONTRIBUTING.md`, `DEVELOPERS.md`, `CODE_OF_CONDUCT.md`, `SUPPORT.md`, `.github/pull_request_template.md`, `.github/ISSUE_TEMPLATE/feature_request.yml`; modify `README.md`, `CHANGELOG.md` and `docs/validation.md`.

**Interfaces:** Documentation commands match the Task 3/4 public interfaces. Third-party notices refer to the actual Task 1 inventory and shipped materials; verification statements cite actual recorded evidence.

- [ ] Copy the approved AGPL-3.0 license text from Procsight; retain provenance for adapted engine code. Do not copy Procsight's third-party inventory or app-specific copyright assertions.
- [ ] Adapt community docs to Agenda: secret ICS URLs, personal event data, encrypted caches, provider sign-in, user-owned manual authentication, notification behavior and synthetic reproduction data. Describe only observed private-reporting availability or explicitly state it is unconfirmed; invent no private email or guaranteed response time.
- [ ] Move contributor build/package detail into DEVELOPERS, link from README, retain all user-facing calendar instructions and document `-Format Msi`/Msix and x64/ARM64 commands. Explain standard machine install/UAC, normal non-elevated app launch, data retention and MSI/MSIX coexistence limits verified from Agenda.
- [ ] Add focused PR/feature templates and an Unreleased changelog entry without changing release version/tag. Record packaging acceptance gaps in docs/validation.
- [ ] Check all changed local document links and command examples and run `git diff --check`. Confirm docs contain no stale ProcSight product text except explicit provenance. Commit docs/templates.

### Task 6: Add standard verification, CI artifacts and end-to-end evidence

**Files:** Create `scripts/verify.ps1`; modify `scripts/test.ps1`, `.github/workflows/ci.yml`, `docs/validation.md`, `DEVELOPERS.md` as required.

**Interfaces:** `scripts/verify.ps1 -Configuration Debug|Release -Architecture x64|ARM64` runs synthetic engine/adapter/dependency checks, existing three Agenda test executables and the requested architecture's app build. Tests execute using the host-compatible RID; cross-building ARM64 does not launch ARM64 payloads on x64.

- [ ] Add checks for verify error propagation and real script syntax/parameter loading. Use the existing console test executables rather than switching them to a different test framework.
- [ ] Implement verify orchestration with explicit native exit-code handling and whitespace checks. Preserve standalone test/build commands and avoid redundant restore/build passes where the same outputs can be reused.
- [ ] Update CI using live-reviewed upstream Action pins: run verification and produce x64/ARM64 MSI plus manifests as downloadable artifacts under read-only permissions. Preserve MSIX regression checks, omit certificate trust and avoid product installation or actual release publication.
- [ ] Run `./scripts/verify.ps1 -Configuration Release -Architecture x64`, an ARM64 Release build, and real package generation/inspection for both formats and architectures. Record skipped/incompatible checks explicitly.
- [ ] Run `./scripts/test-portable.ps1 -Directory <published-x64-folder>` with Agenda closed and a synthetic profile. Find available disposable Windows/ICE tooling; run lifecycle and reminders checks there only if available. Otherwise record the gap without installing on the user's host.
- [ ] Review final changes against all spec requirements, compare provenance and final hashes, check docs/schema examples, run `git diff --check`, and record actual results. Obtain a fresh whole-change review using the chosen execution method; resolve actionable findings with focused retests.
- [ ] Commit verification/CI/evidence changes and report artifacts, checks and outstanding installed-lifecycle limitations. Do not push or publish.

## Execution handoff

Plan self-review: each spec section maps to Tasks 1–6; the five Review Focus cases
have explicit owner tasks/tests. Engine functions are confined to `tools/msi/` and
consume no Agenda adapters. Signing/provenance/dependency collection remain outside
that boundary. The initial implementation introduces no packaging framework or
parser dependency; dependency updates remain evidence-driven.

Recommended method: native execution in this session, followed by one fresh
whole-change review. These tasks share the configuration/payload interfaces and a
single Windows packaging environment, so serial implementation makes integration
and failure diagnosis straightforward. Subagent execution is an alternative if
the user prefers task-by-task independent implementation and review.
