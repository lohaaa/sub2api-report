# Public Repository Data Policy

This repository is public. Documentation, source code, tests, fixtures, screenshots, issues, logs, and release artifacts must not contain private or organization-identifying data.

## Prohibited content

- Chat transcripts or screenshots from private conversations
- Real names, personal email addresses, phone numbers, or user IDs
- Organization names, internal domains, hostnames, IP addresses, or topology details
- Real API keys, webhook URLs, SMTP credentials, cookies, tokens, or passwords
- Production usage records or reports that can identify a person or organization
- Private repository URLs, ticket links, or customer data

## Required practices

- Use anonymous examples such as `用户 A`, `user-a`, and `recipient@example.com`.
- Use reserved example domains such as `example.com`.
- Use obvious placeholders such as `<admin-api-key>` and `<webhook-secret>`.
- Keep local configuration, generated reports, databases, backups, logs, screenshots, and secrets out of Git.
- Sanitize bug reports and test fixtures before committing them.
- Review staged changes for secrets and identifying data before every commit.

## Repository safeguards

The implementation must include:

- `.gitignore` rules for `.env`, SQLite data, backups, reports, logs, screenshots, and local secrets;
- secret scanning in CI;
- synthetic test fixtures only;
- masked secrets in application logs and API responses;
- an export warning that generated reports may contain personal usage information and must not be attached to public issues.

If sensitive data is committed, remove it from the working tree immediately, rotate affected credentials, and follow GitHub's sensitive-data removal procedure. A normal follow-up commit is not sufficient when a secret exists in Git history.
