# Scripts

Maintenance helpers for this repository. These scripts are not part of the running Docker Compose stack; they support setup and GitHub Actions environment management.

## Files

| File | Purpose |
| --- | --- |
| `copy-env-vars.sh` | Copies GitHub Actions environment variables from one repository environment to another and recreates secret names with placeholder values. |

## `copy-env-vars.sh`

Use this when preparing a GitHub Environment for the deployment workflow in `.github/workflows/publish-home-lab.yaml`.

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
  mouras-home-lab
```

Arguments:

| Argument | Description |
| --- | --- |
| `GITHUB_TOKEN` | Optional token used to authenticate `gh`. Omit it when already logged in with `gh auth login`. |
| `SOURCE_REPO` | Source repository in `owner/repo` format. |
| `SOURCE_ENVIRONMENT` | Source GitHub Actions Environment name. |
| `TARGET_REPO` | Target repository in `owner/repo` format. |
| `TARGET_ENVIRONMENT` | Target GitHub Actions Environment name. |

## Requirements

- `bash`
- Homebrew (`brew`)
- GitHub CLI (`gh`)
- `jq`

The script checks for `gh` and `jq` and installs missing tools with Homebrew. It exits if Homebrew is not installed.

## Important Notes

GitHub does not allow stored secret values to be read back in plaintext. This script can list secret names from the source environment, but it cannot copy the real secret values. Target secrets are created with `REPLACE_WITH_SECRET_VALUE`, so replace those placeholders in GitHub before running a deployment.

The script overwrites target variables and secrets with matching names. Confirm the source and target repositories before running it.
