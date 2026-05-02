# Scripts

Helper scripts for repository maintenance and deployment workflows.

## `copy-env-vars.sh`

Copies GitHub Actions environment variable and secret names from one repository environment to another.

```sh
./scripts/copy-env-vars.sh \
  [GITHUB_TOKEN] \
  <SOURCE_REPO> \
  <SOURCE_ENVIRONMENT> \
  <TARGET_REPO> \
  <TARGET_ENVIRONMENT>
```

Example:

```sh
./scripts/copy-env-vars.sh \
  ghp_exampletoken \
  moura/source-repo \
  production \
  moura/target-repo \
  production
```

Arguments:

| Argument | Description |
| --- | --- |
| `GITHUB_TOKEN` | (Optional) GitHub token used for API requests. If not provided, you must be already logged in via `gh auth login`. |
| `SOURCE_REPO` | Source repository in `owner/repo` format. |
| `SOURCE_ENVIRONMENT` | Source GitHub Actions environment name. |
| `TARGET_REPO` | Target repository in `owner/repo` format. |
| `TARGET_ENVIRONMENT` | Target GitHub Actions environment name. |

Dependencies:

- `bash`
- `brew` (Homebrew)
- `jq`

Note: The script will automatically install `gh` (GitHub CLI) if it is not present.

Important note: GitHub Actions secrets cannot be retrieved in plaintext after they are stored. This script can list secret names, but it cannot truly copy secret values from GitHub unless those values are available from another source. Treat the current script as a helper scaffold that may need adjustment before use in a production migration.

Security notes:

- Avoid passing long-lived personal access tokens directly in shell history.
- Prefer short-lived or fine-scoped tokens.
- Confirm the target repository and environment before running, as the script will overwrite existing variables and secrets with the same names.
