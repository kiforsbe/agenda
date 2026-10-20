# Third-party components

Agenda source and the adapted MSI tool are licensed under GNU AGPL version 3.
Dependency licenses remain separate. The full package inventory and current
redistribution findings are recorded in [docs/DEPENDENCIES.md](docs/DEPENDENCIES.md).

Packages contain third-party software and native .NET/Windows runtimes. Generated
payloads include their applicable license and notice materials under Licenses/.
Source licenses do not automatically describe Microsoft binary package terms.
Windows App SDK, Windows SDK, Windows ML and MSAL native interoperability binaries
have package-specific terms; the binary distribution/AGPL compatibility review
remains unresolved. Local inspected build artifacts are not certification that a
public distribution satisfies every applicable condition.

The MSI tool includes native authoring/inspection code adapted from ProcSight;
see [its provenance notice](tools/msi/NOTICE.md).
