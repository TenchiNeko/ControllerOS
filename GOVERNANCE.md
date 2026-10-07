# Repository governance

Repository settings were inspected and recorded on 2026-10-07 before the
Headless Community Bridge changes. The repository remains public, owned by
`TenchiNeko`, with `main` as its default branch. No collaborators, teams,
branches, tags, releases, issues, or historical commits were changed.

## Contribution and merge model

Contributions are proposed through pull requests. The repository does not use a
fixed contributor or team allowlist. Squash merging is preferred; rebase
merging remains enabled, and merge commits are disabled. Auto-merge and
delete-branch-on-merge remain disabled.

## Protection on `main`

The active repository ruleset `main-protection` targets only
`refs/heads/main`. It requires pull requests, prevents force-pushes and branch
deletion, requires the `build-test-format` status check, and requires review
conversation threads to be resolved. It requires zero approvals. It does not
require CODEOWNERS approval, signed commits, deployments, or contributions from
a fixed user or team.

GitHub currently reads `require_extra_approval_for_unattributed_changes` as
enabled by default. GitHub documents that this rule has no effect when the
required approval count is zero, which is the configured policy here.

Before the ruleset was created, the authenticated GitHub CLI user was verified
as `TenchiNeko`, repository owner, and repository admin. The ruleset's bypass
entry was then read back from GitHub: user id `256011908`, bypass mode `always`,
and `current_user_can_bypass=always`. This is the owner recovery path for a
broken rule or urgent repository incident. Use it only for recovery; ordinary
changes should use the PR path and pass CI. No other bypass actor is configured.
The CLI authentication reported `repo`, `read:org`, and `gist` scopes, and the
live repository API confirmed the required administration access. No token
value was recorded.

## CI and Actions permissions

The exact required check name, `build-test-format` (GitHub Actions app id
`15368`), was discovered on live main and passed on a pushed test branch before
it was required. The successful test-branch run was `37636367335` at commit
`3a41dfcffef7b2407340f4b2af15c75e7e5d33f0`. The required `build-test-format`
job runs the Release build, tests, formatting, and Windows artifact publish. It
depends on `windows-cli-smoke`, which runs the same automated suite plus the
headless self-test, including its Windows device-enumeration diagnostic, on a
Windows runner.

GitHub Actions is enabled and allows all actions. Repository default workflow
token permissions are read-only; workflows cannot approve pull requests. The
CI workflow needs only repository contents read access. The Windows artifact
will use the workflow artifact service, with no repository contents write
permission. SHA pinning is not required by the current repository setting.

## Approval policy as maintainers emerge

The approval count remains zero while the project has no independent
maintainer pool. Revisit this after at least two people have repeatedly
reviewed and maintained the project. Consider requiring one approval from a
non-author maintainer first; increase the count only when the active pool can
reliably provide it. Retain the owner recovery bypass so a policy change cannot
lock the repository owner out.
