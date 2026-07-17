# Security Policy

## Supported versions

| Version | Security fixes |
|---|---|
| Latest `0.1.x` preview | Yes |
| Older snapshots | No |

## Reporting a vulnerability

Do not disclose vulnerabilities in public issues, discussions, pull requests, logs, or sample configuration files.

Use the repository's GitHub private vulnerability reporting flow: **Security > Advisories > Report a vulnerability**. If that option is not available, contact the maintainer privately through the verified contact method on the repository owner's GitHub profile and include only enough information to establish a secure follow-up channel.

Include the affected version/commit, platform, impact, reproduction steps, and any suggested mitigation. Do not include real production credentials or user data.

## Scope and expectations

The maintainers will acknowledge a complete report as soon as practical, validate impact, coordinate a fix, and credit reporters when requested and appropriate. Preview software can change while a fix is prepared.

The optional Game API and monetization backends are reference implementations. A downstream game remains responsible for production identity, authorization, store/webhook verification, secrets management, rate limiting, monitoring, data retention, and incident response.
