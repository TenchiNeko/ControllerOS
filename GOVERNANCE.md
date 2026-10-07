# Repository governance

Recorded on 2026-10-07 for the Headless Community Bridge.

## Baseline inspected before changes

- Repository visibility was public and `main` was the default branch.
- The repository owner, `TenchiNeko`, was the authenticated GitHub CLI user
  and had repository `admin` permission. The CLI reported scopes `repo`,
  `read:org`, and `gist`; no credential value is recorded here.
- All three merge methods were enabled: merge commits, squash, and rebase.
  Auto-merge and delete-branch-on-merge were disabled.
- GitHub Actions was enabled and allowed all actions. The default workflow
  token was read-only, and Actions could not approve pull requests. SHA
  pinning was not required.
- `main` had no branch-protection rule or repository ruleset.
- The existing CI check on the latest main commit was the GitHub Actions check
  `build-test-format`; it completed successfully. A pushed test-branch run is
  still required before adding it as a merge requirement.

## Contribution and merge model

Community members may propose changes through pull requests. The repository
does not use a fixed contributor or team allowlist, and this configuration
does not add collaborators or teams. Squash merging is preferred; rebase
merging remains available, while merge commits are disabled.

## Planned main protection

After the exact CI check passes on a test branch, the `main` ruleset will
require pull requests, that GitHub Actions check, and resolved review
conversations. It will block force pushes and branch deletion. It will require
zero approvals, not require CODEOWNERS approval or signed commits, and not
restrict who may contribute.

The proposed ruleset includes an explicit always-bypass entry for the
repository owner. The authenticated owner matches the repository owner and
has admin permission; the entry will be read back from GitHub after creation
to verify recovery access. Use the bypass only to recover from a broken rule
or urgent repository incident; do not use it as the ordinary merge path.

## CI and Actions permissions

The existing `build-test-format` check runs restore, Release build, tests, and
formatting/analyzer verification. It was read from the live check run and
workflow job. It will become required only after the pushed test-branch run
passes.

The current workflow only needs repository contents read access. The
repository's default `GITHUB_TOKEN` permissions remain read-only. The planned
artifact upload will use the workflow-run artifact mechanism and will not
grant repository contents write access. If a future workflow needs additional
permission, add only that specific permission to that workflow and explain it
here.

## Approval policy as maintainers emerge

Approval count remains zero while the project has no independent maintainer
pool. Revisit this after at least two people have repeatedly reviewed and
maintained the project. At that point, consider requiring one approval from a
non-author maintainer, then raise the count only if the active maintainer pool
can reliably provide it. Keep the owner emergency bypass so a policy change
cannot lock the repository owner out.
