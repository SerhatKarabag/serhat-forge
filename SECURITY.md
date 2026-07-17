# Security Policy

## Supported versions

| Version | Security fixes |
|---|---|
| Latest `0.1.x` preview | Yes |
| Older snapshots | No |

## Reporting a vulnerability

Do not disclose vulnerabilities in public issues, discussions, pull requests, logs, or sample configuration files.

For a public Serhat Forge repository, use GitHub private vulnerability reporting: **Security > Advisories > Report a vulnerability**. GitHub exposes this setting only after the repository is public, so complete the code and documentation gates while private; then make the repository public, immediately enable private vulnerability reporting in the repository's code-security settings, and verify that the **Report a vulnerability** button is available before announcing or sharing the release.

While the repository is private, coordinate only through an already authorized private collaborator channel. If no private channel is available, do not publish exploit details, credentials, or player data; wait until the maintainer provides a secure reporting path.

Include the affected version/commit, platform, impact, reproduction steps, and any suggested mitigation. Do not include real production credentials or user data.

## Scope and expectations

The maintainers will acknowledge a complete report as soon as practical, validate impact, coordinate a fix, and credit reporters when requested and appropriate. Preview software can change while a fix is prepared.

The optional Game API and monetization backends are reference implementations. A downstream game remains responsible for production identity, authorization, store/webhook verification, secrets management, rate limiting, monitoring, data retention, and incident response.
