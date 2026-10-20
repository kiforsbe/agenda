# Developing Agenda

Agenda uses C#, WinUI 3 and .NET 10 on Windows 11. Use PowerShell 7.4 or later, the .NET 10
SDK and Windows SDK 10.0.26100.0 or later. The application runtime floor is .NET
10.0.13; self-contained packages bundle it. Windows App SDK restores from NuGet.
See [ARCHITECTURE.md](ARCHITECTURE.md) for component/runtime boundaries.

```powershell
./scripts/build.ps1 -Configuration Release -Architecture x64
./scripts/run.ps1
./scripts/run.ps1 -DataDirectory ./work/profile
./scripts/test.ps1
./scripts/verify.ps1 -Configuration Release -Architecture x64
```

The existing tests are console executables, not dotnet test projects. verify.ps1
runs their actual entry points plus native MSI, configuration, dependency-report,
adapter and whitespace checks. A cross-build does not establish ARM64 runtime
acceptance. Authentication and real calendar/provider acceptance remain manual.

## Packages

```powershell
./scripts/package.ps1 -Format Msi -Architecture x64
./scripts/package.ps1 -Format Msi -Architecture ARM64
./scripts/package.ps1 -Format Msix -Architecture x64
./scripts/package.ps1 -Format Msix -Architecture ARM64
```

Msi is the default. MSIX requires an explicit -Format Msix. Each build gets isolated output/intermediate/staging
directories under artifacts/packages/FORMAT/VERSION/ARCHITECTURE/BUILD-ID.
MSI output and release-artifacts.json are inside its installer subdirectory.
MSIX filenames end in -unsigned.msix or -signed.msix. No fixed-name alias is
refreshed; use the returned Artifact/Manifest paths.
Full build-tool diagnostics are saved in tool-output.log at the build directory
root; the console shows concise success summaries and failure tails.
The manifest records version, source commit, format, architecture, signing state
and the final artifact SHA-256. SourceDirty explicitly marks uncommitted changes.
Packaging a dirty checkout records HEAD, not an
assertion that uncommitted source matches that commit. Release builds should be
made from a clean reviewed revision.

Both formats are unsigned by default. Packaging never creates signing keys,
exports certificates or imports trust. MSI installs under Program Files and uses
a Start-menu shortcut; close Agenda before maintenance. Uninstall preserves
per-user data, and normal app launch does not elevate the application.

Select a certificate explicitly, matching Procsight's thumbprint workflow:

```powershell
./scripts/package.ps1 -Format Msix -CertificateThumbprint YOUR_40_HEX_THUMBPRINT
./scripts/package.ps1 -CertificatePath C:/signing/publisher.pfx
```

-CertificateThumbprint selects exactly one existing certificate from CurrentUser/My.
-CertificatePath is an alternative explicit PFX input; it cannot be combined with
a thumbprint. -CertificatePassword requires a PFX. Optional -TimestampUri must
be HTTPS and requires signing. Invalid selections fail before restore/publish.
MSIX additionally requires the certificate subject to match -Publisher, whose
development default is CN=Agenda.Development.

For a development MSIX, creation/inspection and trust are separate operations:

```powershell
$certificate = ./scripts/setup-dev-certificate.ps1
$package = ./scripts/package.ps1 -Format Msix -CertificateThumbprint $certificate.Thumbprint
# Trust only the reviewed public certificate, in an administrator shell:
./scripts/trust-dev-certificate.ps1 -CertificatePath $certificate.PublicCertificate
# Install the returned package path in the intended user context:
Add-AppxPackage $package.Artifact
```

The certificate helper returns Thumbprint, Publisher and PublicCertificate.
It can reuse a matching valid development certificate; a newly created key is
non-exportable and lasts one year. The packaging command itself never creates
or selects certificates by subject and no longer accepts -UseCertificateStore.
The former fixed-name MSIX alias is not refreshed. Unsigned MSIX is inspected
build output, not installable until explicitly signed with a trusted matching
certificate. Public signing and Store publication remain separate decisions.

Publishing preserves Agenda's compiled XAML/PRI files and the Windows App Runtime
notification resource workaround. Both container formats are inspected against
the published payload. Actual reminder delivery/activation needs installed-host
testing. No automatic MSI/MSIX data migration or supported coexistence is claimed.

## Shared MSI engine

[tools/msi](tools/msi/README.md) owns generic authoring/inspection and standalone
tests. [deployment/msi.json](deployment/msi.json) owns Agenda metadata and payload
approval. Keep its product namespace stable. The adapter owns .NET publish,
resource checks, dependency audit, license collection, signing and Git provenance.
A second consumer needs only the engine directory and its own JSON configuration.
The future shared repository will replace a pinned local engine; no automatic
download/update mechanism is present.

## Dependency review and updates

Review live NuGet registry and upstream guidance for the latest listed stable
compatible versions, replacements/deprecations, all severity advisories and
actual package license/redistribution terms. Include every resolved transitive,
native dependency and bundled runtime. NuGet lower bounds, lock files and
successful restore do not establish currency or safety. Build-only tools and
GitHub Actions also need version/upstream review.

```powershell
dotnet list src/Agenda.App package --outdated --include-transitive
dotnet list src/Agenda.App package --deprecated --include-transitive
dotnet list src/Agenda.App package --vulnerable --include-transitive
```

Run these across all projects and review RID-specific graphs. Packaging forces an
audited restore and treats advisory retrieval failures and every advisory severity
as errors. [docs/DEPENDENCIES.md](docs/DEPENDENCIES.md) records the reviewed set,
exceptions and package terms. Preserve notices and vendored version-specific
license texts. Do not describe an advisory scan as complete security assurance.

## Versioning and validation

AgendaVersion in Directory.Build.props remains authoritative. Update the matching
CHANGELOG heading and tag together; check with scripts/check-release.ps1 -Tag
vMAJOR.MINOR.PATCH. MSI uses three version fields, maximum 255.255.65534; assembly
and MSIX versions append .0. No release version, tag or publication is created by
packaging.

Record exact results and unresolved checks in [docs/validation.md](docs/validation.md).
Synthetic MSI tests include native read-only preflight on the matching host and
byte comparison, not actual installation. Use disposable compatible Windows hosts
for install/repair/upgrade/downgrade/uninstall and installed notification checks;
do not install test products or certificates on a developer's host automatically.
