# Validation — 2026-09-10

## Verified on this machine

- Current-month pinning follow-up (2026-09-11): 23 core checks passed, including retaining today's events while browsing distant months, retaining stale data until refreshed, clearing snapshots, and respecting deletions across overlapping refreshes.
- Month buffer follow-up (2026-09-11): all 21 core checks passed, including neighboring-grid coverage across year boundaries, refill boundaries, regional weekdays, and Persian calendar months. These checks validate date coverage, not measured scrolling latency.
- Windows 11 x64, build 26200; .NET SDK 10.0.301 and Windows SDK 26100.
- Native WinUI app build: zero warnings and errors.
- `scripts/test.ps1`: 19 calendar core checks, 33 provider checks, 13 shell checks passed, exit 0. Provider checks use deterministic local fixtures and mocked transport/authentication, not personal accounts.
- Core coverage includes Swedish and US week rules, first-weekday overrides, ISO year rollover, Persian regional weeks, timezone overlap and exclusive all-day end dates.
- Provider coverage includes Graph paging and consent boundaries, read-only rejection, encrypted caches and sign-out, ICS recurrence exceptions/replacements, all-day overlap, private URL redaction, partial feed failures, HTTPS redirects, download/evaluation limits and offline fallback.
- Native shell smoke: window positioning/show/hide, tray lifecycle and Ctrl+Alt+A registration passed. Clock discovery reported no independently bounded Win32 clock target; Windows retains normal clock clicks.
- Actual WinUI executable launched and its Agenda window was enumerated. Windows appointment-store probe returned one calendar and zero events for the range from seven days before the probe through thirty days after it. Event titles and account details were not written to the probe log.
- `scripts/package.ps1 -Architecture x64 -UseCertificateStore` produced a self-contained portable directory and signed `artifacts/Agenda-x64.msix`; MakeAppx validation, signing, and packaging succeeded. The development signing certificate is stored in the current user's Personal store and its public `.cer` is kept under ignored `artifacts/signing/`; the corrected trust helper requires administrator privileges and targets Local Machine Trusted People. The previous current-user trust workflow passed SignTool verification but failed actual installation with 0x800B0109.
- Portable startup regression: first failed because publishing omitted `Agenda.App.pri` and compiled XAML. The publish target now includes those resources explicitly. `scripts/test-portable.ps1` then passed: the native process remained running without logged exceptions. This matches the upstream [unpackaged publish resource issue](https://github.com/microsoft/WindowsAppSDK/issues/6720).
- ARM64 Release configuration compiled with zero warnings and errors; it was not executed on ARM hardware. Signed ARM64 packaging and clean-machine installation remain unverified.

## Review changes

Independent code review identified stale OS calendar enumeration, a midnight reopening issue, stale event ranges after regional changes, and suspend visibility tracking. The app now re-enumerates Windows calendars, rerenders when opened, refreshes changed ranges, and queries actual native visibility when toggled. Connection changes validate/save before replacing a working provider.

## Follow-up: tray and visual fixes

- Reproduced duplicate tray activation in the real callback: one version-4 click sequence caused two toggles. The callback now separates version-4 semantic notifications from legacy mouse notifications. The regression and ten existing shell checks pass.
- Day buttons now have equal 40-DIP dimensions. Each event marker has its own layout row and a 6-DIP diameter. Live native inspection verified circular today/selected states and clear markers below the date numbers.
- Removed the extra root tint layer. The user confirmed the diagonal artifact was gone.
- WinUI retained a classic dialog frame even while its presenter reported `HasBorder=false`. A scoped subclass of Agenda's own window now makes the entire rectangle client content through `WM_NCCALCSIZE`; DWM supplies rounded corners. The native regression first reported a 184-pixel client width inside a 200-pixel window, then passed with both dimensions equal. Live screenshots verified the white frame is gone and acrylic remains.
- Selected an empty day in the live app and verified the circular selection ring, agenda update and stable layout. Independent review checked the subclass lifetime and pointer-sized interop.
- Repeated physical tray clicks and the exact ordering of outside-click dismissal still need interactive confirmation; the automated callback test verifies duplicate-notification handling and the delayed-dismissal path.

## Remaining manual checks

- Event navigation: verify Windows appointment details, Microsoft Graph event links, HTTPS ICS VEVENT URLs, and the in-app fallback details view interactively. ICS feeds do not always provide event deep links; in those cases the details view can offer an inferred Google/Outlook or custom source-calendar website. No live calendar website or Windows appointment was opened by the automated checks.
- Feed reorder follow-up (2026-09-11): Release x64 build passed with zero warnings/errors; all 35 provider checks passed, including encrypted order round-tripping and rejection of incomplete/duplicate orders. Interactive drag smoothness, row dragging while preserving button actions, Escape/outside-drop cancellation, edge scrolling, and Alt+Up/Down still need a live desktop check.

- Initial screenshot inspection was blocked by an approval timeout; follow-up inspection succeeded as recorded above. Full keyboard and screen-reader coverage is still pending.
- User-configured feeds were visible during follow-up inspection. Provider parsing/download/cache behavior is tested with fixtures; end-to-end freshness and changes at each remote calendar service still need checking.
- Microsoft WAM/Graph live sign-in and event creation require an app registration and explicit user consent. No real calendar entries were created or changed.
- Multiple monitor hardware, HDR, high contrast, screen readers, taskbar auto-hide and Explorer restart need interactive validation. Geometry and strict clock hit testing have automated coverage.
- ARM64 runtime, x64 signed MSIX installation, and clean-machine installation have not been verified. The package was signed but not installed by the validation run. No startup registration was added.

## Try the app

Run `scripts/build.ps1`, then `scripts/run.ps1`. Open Agenda from its notification-area icon or Ctrl+Alt+A. In Settings choose Manage iCalendar feeds; Add calendar feed opens the entry popup. Other provider setup controls are hidden, and taskbar-clock interception is disabled. Week numbers are enabled by default; ISO mode explicitly forces Monday and the ISO first-week rule.

After packaging, exit other Agenda instances and run `scripts/test-portable.ps1 -Directory <the portable directory printed by package.ps1>` to check resource inclusion and startup. This check launches and then closes its own isolated test instance.

For an isolated profile use `scripts/run.ps1 -DataDirectory ./work/manual-profile`. Exit any running Agenda instance first: one instance runs per Windows session. Right-click its notification icon and choose Exit.

## Generic MSI tooling and build/documentation integration — 2026-10-20

Environment: Windows x64 build 26300, PowerShell 7.6.5, Windows SDK tools 26100,
and current stable .NET SDK 10.0.402 staged locally after verifying the official
archive SHA-512. Initial baseline also passed on installed SDK 10.0.301.

Executed successfully:

- scripts/verify.ps1 -Configuration Release -Architecture x64: configuration checks (27), native synthetic MSI checks (39), dependency-report, packaging-adapter and verification entry-point checks, all existing core/provider/shell executables, Release build and Git whitespace checks. Native MSI tests do not install any product.
- scripts/build.ps1 -Configuration Release -Architecture ARM64: locked restore and Release cross-build, zero warnings/errors. Tests run on the host-compatible runtime.
- scripts/package.ps1 for Msi/Msix and x64/ARM64: actual self-contained publishes and inspected containers. MSI cabinet files are hash-compared with published files; MSIX is unpacked and staged-file hashes compared. Development MSIX signing succeeds; private temporary keys are removed without importing trust.
- test-portable.ps1 against the x64 MSI payload, with CheckNotifications: process remains running, no errors.log, and native AppNotificationManager registration logs Enabled. This does not deliver or activate a reminder.
- Standalone engine copied outside Agenda into a path containing spaces, then invoked with a second synthetic product configuration; native package creation/inspection succeeds without repository or WinUI assumptions.
- Public engine Get-Help synopsis resolves; 46 local documentation targets checked; all changed PowerShell scripts parse.
- Live NuGet inventory records 32 resolved package/version pairs including transitive, target-reference and runtime-pack dependencies. No current NuGet advisory/deprecation findings. Action graphs have explicit known advisory/deprecation exceptions; see docs/DEPENDENCIES.md.

Limits and remaining acceptance:

- No disposable Windows Sandbox/VM or installed ICE command was available. A Windows SDK MsiVal2 installer exists, but it was not installed on the host. Real install/repair/upgrade/downgrade/uninstall, advertised-shortcut maintenance, interrupted-install rollback and minimum-build rejection remain unverified.
- ARM64 packages were cross-built and inspected; ARM64 native execution and read-only native preflight require an ARM64 host.
- Real installed reminder delivery, notification activation, user-data retention after uninstall, MSI/MSIX coexistence and real provider authentication remain separate manual checks.
- No product was installed, no certificate trust was imported, no tag/release was created and no GitHub workflow was run. CI was inspected locally; hosted runner execution remains outstanding.
- Root AGPL source licensing does not resolve Microsoft binary redistribution conditions. SDK/Windows App SDK/ML/NativeInterop terms are preserved and explicit unresolved public distribution findings are recorded in docs/DEPENDENCIES.md.
- Bundled Action dependency scans report current advisories and an upstream glob deprecation. Those are recorded upstream exceptions, not a clean-bill-of-health claim.

### Independent review follow-up

A fresh read-only review found three portability/isolation defects and two
supported-floor/MSI-alias issues. All five were corrected in one fix pass:

- Resolve public paths against the PowerShell filesystem location, including after Set-Location; normalize exact approval paths across slash styles.
- Remove the redundant default-context package-list query during packaging. The authoritative advisory check is the isolated, forced restore with all severity levels and retrieval failures promoted to errors. Deliberate standalone report queries now use --no-restore.
- Require PowerShell 7.4+, preserving supported built-in crypto/JSON APIs.
- Generate 8.3|long aliases for install/menu roots and shortcuts; inspect every configured shortcut mapping.

Regression evidence: the added relative-location, exact-backslash, no-restore
and root-alias cases first failed against the old implementation. Configuration
checks now pass 29 cases and native MSI checks pass 43. The full x64 verification
suite passes again with zero build warnings/errors. Public Get-Help and standalone
maintenance/file-map documentation were also checked.

The real post-fix ARM64 MSI also builds and passes cabinet inspection. A before/
after guard over all 15 top-level normal src/*/obj files confirms identical bytes
and last-write timestamps, proving packaging no longer performs a default-context
development restore.

## MSI default and explicit MSIX signing — 2026-10-20

The user approved MSI as the default and a Procsight-style explicit MSIX build/
signing process. Both formats now default to unsigned output. Package creation
does not create keys, export certificates or import trust. -CertificateThumbprint
selects one existing CurrentUser/My certificate; an explicit PFX remains an
alternative. Certificate creation/trust are separate commands.

Verified:

- Updated packaging tests first failed against the old MSIX default, then passed after changing the default to MSI. Tests cover unsigned MSIX metadata, signing-failure behavior, conflicting selectors, orphan passwords/timestamps, invalid thumbprints, HTTP timestamps, publisher mismatch, missing private keys, expiry and wrong EKU.
- Full verification passed: 29 JSON/configuration checks, 43 native synthetic MSI checks, dependency-report, adapter and verification-entry tests, all Agenda test executables and Release build with zero warnings/errors.
- Actual x64 opt-in MSIX publish/pack/unpack inspection passed, final SHA-256 matched, Get-AuthenticodeSignature reported NotSigned and CurrentUser/My thumbprints were identical before/after. No certificate was created or imported.
- Actual x64 packaging without a Format argument produced an unsigned MSI, passed cabinet inspection and final SHA-256 verification, retained tool-output.log and left CurrentUser/My thumbprints unchanged.
- Tool logging has a failing-then-passing regression: full output is saved in per-build tool-output.log with concise console summaries. Signing arguments/passwords never go through that logger.
- Public package Get-Help, 27 current documentation links and all 18 Agenda script parsers passed. CI explicitly builds and validates unsigned opt-in MSIX metadata.

A live thumbprint-signing test using a temporary test certificate was blocked
by automatic approval review before execution (stated reason: blocked by policy).
No test certificate was created. Live thumbprint signing remains unverified;
metadata/option validation and signing-failure behavior are covered by tests.
The prior installed lifecycle/ICE, ARM64 execution and redistribution limitations
remain unchanged.

## External winbuildtools integration — 2026-10-22

The requested build-tools release 0.1.0 is published as tag v0.1.0, resolving to
5ebe3c79ec35cfbcd015eb1de17848602375f883. The HTTPS submodule at
external/winbuildtools records that exact commit; Agenda's application version
remains 0.4.0. Local MSI authoring and MSIX packing/inspection implementations
were removed. Agenda owns product configuration, manifest translation, publish
resources, signing and final hash metadata. New planning is outside Git.

Live upstream review inspected the remote tags, release source/changelog/security
guidance, MIT license and attribution. No other stable tags were listed. The
public GitHub repository advisory endpoint returned zero published records on
2026-10-22; this does not rule out private/unknown issues. The release declares
no runtime package dependencies, vendored executables or nested submodules.
Existing platform prerequisites, application dependency exceptions and public
redistribution limitations are unchanged.

The adapter tests first failed because the shared adapter did not exist, then
passed with the implementation. They cover a missing/incomplete checkout before
restore/publish, the exact tool commit, unchanged default format/application
version, product version/architecture/publisher translation, an external manifest
and an unchanged portable publish directory. The shared tool suite and all Agenda
tests plus a Release x64 build pass, with zero build warnings/errors. The separate
read-only integration review reported no actionable findings. CI remains manual
only and now initializes recursive submodules.

Real Agenda MSI and unsigned MSIX packages for x64 and ARM64 all passed the
external engine's database/cabinet or manifest/payload inspection. All four final
Agenda hash manifests matched the actual artifact bytes. Application version
remained 0.4.0; MSIX retained the Agenda-named output and portable publishing
remained separate from the external stage. Logs/results are under ignored
artifacts/verification/winbuildtools. All 19 Agenda scripts parsed and 32 current
documentation links resolved. These are local unsigned builds, not installed-host
acceptance or public redistribution approval; prior limitations remain applicable.

Automatic approval review rejected the fresh-recursive-clone smoke-test command
before execution (reason: blocked by policy). No checkout fixture was created by
that command. A fresh-clone end-to-end check and hosted Actions execution remain
unverified; local initialized-submodule builds, missing-checkout preflight,
exact-release-pin checks and the four real package inspections passed.

## Complete shared packaging delegation — 2026-10-22

The winbuildtools project chat updated consumer guidance and published v0.2.0 at
99d60cf6f0ef4b823f55237e664fc02684bb001d, annotated tag object
a678931f9dd7cc470aaf77e5db5c30aee21b9047. Remote tag/commit were checked before
updating Agenda's gitlink. Shared implementation directories and MIT license are
identical to v0.1.0; the example's executable tokens are unchanged. Upstream
instructions now explicitly require delegation of all applicable packaging
mechanisms, not just installer creation. No hosted workflow was dispatched.

Agenda now has scripts/package.ps1 plus one product adapter,
scripts/packaging/Agenda.ps1. Local Common/Installers/Signing implementations and
certificate setup/trust scripts were deleted. Publishing/resource/license policy
and product fields remain local; SDK discovery, logs, certificate checks/signing
and final hashes/signature state come from the external checkout. Shared metadata
is preserved and augmented with Format/Architecture and legacy Commit. New
SourceCommit/SignatureStatus/SignerThumbprint fields describe the shared result.

Delegation tests failed on the missing product-only adapter before implementation
and now pass. They check shared function source ownership, removal of duplicate
files, signing failure without a success manifest, actual final-byte/signature
metadata rather than the requested Sign flag, preserved product fields, unsigned
defaults/options and a no-mutation certificate -WhatIf path. No key is created,
trusted or used by these checks. The separate read-only cleanup review reported
no actionable findings. Live signing/trust remains unverified.

Final local verification against the published v0.2.0 pin passed: 29 shared
configuration checks, 43 native MSI checks, shared MSIX/signing/common checks,
Agenda delegation/adapter/dependency checks, application test executables and
Release x64 build with zero warnings/errors. All four real unsigned MSI/MSIX
x64/ARM64 packages passed inspection; final shared hashes matched the artifacts,
Signed was false and SignatureStatus was NotSigned. SourceCommit matched legacy
Commit and Format/Architecture were retained. Agenda version remains 0.4.0.
All 15 Agenda scripts parsed and 36 current documentation links resolved.
Logs/results are under ignored artifacts/verification/winbuildtools-0.2.

The delegated project reports its full local suite and pinned consumer test
passed. Its additional ad-hoc metadata-example fixture was blocked by automatic
approval review before execution; no claim is made for that extra example run.
Agenda's own composition tests and four actual package finalizations did execute
the shared writer plus additive product fields successfully. Hosted workflows
and the prior installed lifecycle/signing/redistribution limits remain separate.
