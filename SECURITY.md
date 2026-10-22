# Security policy

Do not disclose secret calendar-feed URLs, credentials, personal events, account
identifiers or exploit details in public issues. Use GitHub **Security > Report
a vulnerability** if private reporting is enabled; its availability has not been
confirmed. Otherwise request a private maintainer contact without posting the
sensitive details. No private reporting email or response-time guarantee is
configured. Maintainers should establish a private route before public binary
distribution.

Reports should identify the Agenda revision/version, Windows build, installation
format and minimal synthetic reproduction steps. Ordinary bugs belong in the
issue templates; see [SUPPORT.md](SUPPORT.md).

## Boundaries and limitations

Agenda is a Windows calendar tray application. Saved feed links and event caches
use per-user Windows-protected encryption. Secret ICS links should be treated as
passwords. Provider sign-in must be performed by the user; do not submit tokens,
authorization URLs, cache directories or real calendar files in reports.
Notifications can reveal event information on the desktop/lock screen subject to
Windows notification settings. The app should run with ordinary user privileges.

MSI installs per machine into Program Files with Windows Installer elevation;
the app itself is not automatically started or elevated. Uninstall preserves
per-user settings, feeds, caches and credentials. MSIX uses Windows package
identity; no automatic cross-format migration or coexistence guarantee is made.

Packaging performs an audited restore of direct/transitive dependencies, rejects
known advisory findings and advisory retrieval failures, and inspects the
container/payload. These checks are not a complete security audit and do not
prove absence of unknown vulnerabilities or native runtime flaws. Optional MSI
signing signs its container, not every bundled binary. A signature does not
establish reputation, license compatibility or safe application behavior.

Both formats build unsigned unless an existing signing certificate is explicitly
selected. Packaging never creates signing keys, exports certificates or imports
trust. Development certificate creation and trust are separate commands.
These mechanisms use the pinned winbuildtools checkout; there is no local
certificate/signing implementation. Its trust utility requires an explicit
reviewed thumbprint. Shared signing rejects unknown/error signature states;
only Valid/NotTrusted integrity states are accepted. Public trust is a separate
policy decision. Final Signed/SignatureStatus fields describe measured bytes,
not the user's requested signing switch.
Development MSIX signing never imports certificate trust. MSI needs no
development certificate import, but Windows reputation or managed policy can
block unsigned installers. Real install/repair/upgrade/uninstall and notification
acceptance are distinct from synthetic database checks. See
[validation](docs/validation.md) and [dependency review](docs/DEPENDENCIES.md).

## Supported versions

Agenda is under active development. No security maintenance period for older
releases is promised; report the affected revision and test the latest source
where practical without including private data.
