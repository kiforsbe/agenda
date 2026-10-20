# Dependency and redistribution review

Reviewed 2026-10-20 against live NuGet registration/index endpoints, upstream
release guidance and the actual restored package materials. The machine-readable
[inventory](dependency-inventory.json) records all project graph packages,
transitives, selected runtime packs, declared terms and package license hashes.
It also retains the outdated/deprecated/vulnerable reports for every project.
Refresh it deliberately using scripts/review-dependencies.ps1 after restoring
all projects and inspecting both packaging RIDs.

## Selected versions and compatibility

- Windows App SDK 2.4.0 -> 2.5.1, the latest listed stable metapackage; its component
  set resolves Base 2.0.4, Foundation 2.3.12, InteractiveExperiences 2.1.9,
  WinUI 2.3.9, Runtime 2.5.1, AI/Search 2.5.5, ML 2.1.94, DWrite 2.1.0,
  Widgets 2.0.5. These component numbers differ by design.
- MSAL Client/Broker 4.88.0 -> 4.91.1; NativeInterop remains 0.20.6, its latest
  listed stable version. IdentityModel.Abstractions 8.14.0 -> 8.23.0.
- Ical.Net remains 5.2.3, its latest listed stable version; its NodaTime lower
  bound 3.2.2 is explicitly serviced to stable 3.3.5.
- ProtectedData 10.0.0 -> 10.0.13; System.Numerics.Tensors 9.0.0 -> 10.0.13.
- WebView2 1.0.3719.77 -> 1.0.4258.31; Windows ML runtime 2.1.74 -> 2.4.89.
  Agenda does not add a browser or ML feature; these remain restored SDK
  dependencies. Their upstream license material is included if packaged.
- Windows SDK BuildTools 10.0.26100.4654 -> 10.0.28000.2705.
  BuildTools.MSIX 1.7.251221100 -> 1.7.260930100. The index also contains
  1.7.261008100, but its registration metadata says listed=false; it was rejected.
- Windows SDK .NET reference packs use latest listed servicing .87 for each
  existing target: 10.0.19041.87 and 10.0.26100.87. The overall newest pack is
  10.0.28000.87, which belongs to another Windows target; no target/API upgrade is
  needed for installer work.
- The self-contained application explicitly selects .NET 10.0.13 through
  FrameworkReference metadata. Setting RuntimeFrameworkVersion globally is
  incorrect because it also changes Windows SDK framework resolution.
  The preinstalled SDK 10.0.301 was used for initial baseline checks; the current
  stable SDK 10.0.402 is also staged in an ignored local directory for final
  validation. Its official archive SHA-512 is verified against .NET release
  metadata. No system SDK was replaced. CI requests the latest stable 10.0 SDK.

NuGet's project reports found no known advisory or deprecation findings for the
reviewed set. Registry checks also reject unlisted, deprecated, prerelease or
known-vulnerable selected entries. These checks do not detect every native
binary/runtime flaw, unpublished advisory or application vulnerability.
Lock files capture the resolved graphs; they are not evidence of security.

Authoritative upstream references:
[Windows App SDK stable channel](https://learn.microsoft.com/windows/apps/windows-app-sdk/stable-channel),
[MSAL releases](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/releases),
[Ical.Net](https://github.com/ical-org/ical.net),
[NodaTime](https://nodatime.org/),
[.NET 10 servicing](https://dotnet.microsoft.com/download/dotnet/10.0),
[Windows SDK downloads](https://learn.microsoft.com/windows/apps/windows-sdk/downloads).
Exact NuGet package registration URLs are stored in the inventory.

## Actual license and notice materials

| Components | Terms inspected |
| --- | --- |
| Ical.Net, MSAL managed Client/Broker, IdentityModel, .NET libraries/runtime | MIT declarations and upstream/package license texts; preserve their copyright notices |
| NodaTime | Apache-2.0; include complete license and applicable attribution |
| WebView2 SDK | BSD-style three-clause terms in LICENSE.txt, plus NOTICE.txt; browser-runtime terms remain separate |
| Windows App SDK and component/runtime packages | Microsoft Windows App SDK binary terms in each package, including redistribution conditions |
| Windows ML runtime | Microsoft Windows ML Runtime terms plus bundled third-party notices |
| MSAL NativeInterop | Separate Microsoft binary terms and THIRD-PARTY-NOTICES; the managed wrapper's MIT declaration does not replace them |
| Windows SDK tools and .NET reference packs | SDK terms linked by package metadata or supplied as sdk_license.txt; build tools are not application runtime payload |

Some expression-licensed packages omit full license texts. Version-specific
upstream copies for Ical.Net, NodaTime, MSAL and IdentityModel are under
deployment/licenses. sources.json records exact source URLs and SHA-256 hashes.
The SDK link is the actual package-declared https://aka.ms/WinSDKLicenseURL,
whose response is RTF; it is preserved as LICENSE.rtf. The shorter similarly
named WinSDKLicense URL does not provide those terms and was not retained.
Windows SDK reference packages declare the same SDK terms URL.

Packaging collects declared package license files, nested notices and selected
runtime-pack notices. The isolated SDK resolver can enumerate unused Desktop/
ASP.NET runtime candidates; copying their notices is conservative and does not
mean those frameworks are application runtime requirements. The shipped app's
runtimeconfig and payload determine the actual selected .NET runtime.

## Unresolved public redistribution conditions

Agenda source and the ported MSI engine adopt AGPL-3.0. Microsoft binary packages
are not automatically MIT because upstream source repositories are open source.
Windows App SDK/ML terms include distribution requirements and restrictions
relating to source-disclosure obligations. NativeInterop's binary terms include
distribution restrictions. Windows SDK terms also impose conditions on
distributable code. These actual terms have been identified and preserved, but
compatibility and the rights for this particular AGPL binary distribution have
not been established by this engineering review.

Do not describe inspected local artifacts, notices, a successful build or an
advisory scan as approval for public redistribution. Resolve these terms for the
actual binary set before publishing installers. Removing unused broker/ML/SDK
dependencies or obtaining appropriate rights may be follow-up options; neither is
silently assumed or implemented here.

## GitHub Actions

Live upstream release/tag/license checks selected:

| Action | Stable release | Commit | Declared source license |
| --- | --- | --- | --- |
| actions/checkout | v7.0.1 | 3d3c42e5aac5ba805825da76410c181273ba90b1 | MIT |
| actions/setup-dotnet | v6.0.0 | a98b56852c35b8e3190ac28c8c2271da59106c68 | MIT |
| actions/upload-artifact | v7.0.2 | cf430e030ddbb5b0abf93d22962f4752f3646cd9 | MIT |

Actions execute upstream-bundled JavaScript and transitive libraries on GitHub
runners; they are build tooling, not installed Agenda dependencies. Pinned commit
identity and a root license alone do not prove the complete bundled graph is safe.
Review their package locks, bundled dependency notices and public advisories when
updating. The workflow uses read-only repository permissions and does not publish
a release, import trust or install the product. Hosted execution remains to be
observed on GitHub after this change is pushed.

### Explicit upstream Action advisory exceptions

The latest listed stable Action releases above still contain known production
dependency advisories. These findings are retained explicitly as upstream bundle
exceptions, not described as resolved or safe. We do not rewrite a pinned upstream
Action bundle locally; follow up with patched upstream releases or replace the
affected Action before depending on it for a hardened release pipeline.

| Action | Affected bundled package versions | Highest severity |
| --- | --- | --- |
| checkout | undici 6.27.0 | high |
| setup-dotnet | brace-expansion 5.0.6, 1.1.15 | high |
| setup-dotnet | fast-xml-parser 5.9.3 | high |
| setup-dotnet | undici 6.27.0 | high |
| upload-artifact | brace-expansion 1.1.12, 5.0.3, 2.0.2 | high |
| upload-artifact | fast-xml-builder 1.0.0 | high |
| upload-artifact | fast-xml-parser 5.4.1 | high |
| upload-artifact | lodash 4.17.23 | high |
| upload-artifact | undici 6.23.0 | high |

See [full Action lock/advisory evidence](action-dependency-inventory.json) for exact
affected ranges, conditions, advisory links and upstream fixes, and
[npm metadata](action-package-metadata.json) for live registry versions, licenses
and deprecation declarations. Source Action license caches were inspected at the
pinned commits; those caches are not assumed to cover every transitive package.
This remains an explicit review limitation. These dependencies execute in CI,
not in the installed Agenda payload.

upload-artifact also includes glob 10.5.0, which the live npm registry marks
deprecated/unsupported with a security notice. This is an explicit upstream
Action exception, not an application dependency selection. The pinned Action's
production graph is reviewed as supplied; no claim is made that upgrading its
internal packages independently would be supported by upstream.
