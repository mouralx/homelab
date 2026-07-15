# Utility Scripts

Reference documentation for the utility scripts in `scripts/`.

## Contents

- [deepwiki-open.sh](#deepwiki-opensh)
- [copy-env-vars.sh](#copy-env-vars-sh)

---

## deepwiki-open.sh

**Purpose**: Clones, builds, and prepares the DeepWiki-Open Docker image for publishing.

**Location**: `scripts/externals/deepwiki-open.sh`

**Synopsis**:

```sh
sudo ./deepwiki-open.sh
```

**Process**:

1. Refreshes sudo session (requires password argument)
2. Clones `https://github.com/AsyncFuncAI/deepwiki-open.git`
3. Builds Docker image tagged as `deepwiki-open:latest`
4. Cleans up the cloned repository

**Requirements**:
- `sudo` access
- `git` installed
- `docker` installed
- Internet access to GitHub

**Notes**:
- The script does not push the image to any registry by default
- After running, the image is available locally as `deepwiki-open:latest`
- The cloned repository is deleted after the build completes

---

## copy-env-vars.sh

**Purpose**: Copies GitHub Environment variables and secrets from one repository/environment to another.

**Location**: `scripts/utils/copy-env-vars.sh`

**Synopsis**:

```sh
# With existing gh auth
./copy-env-vars.sh <source-repo> <source-environment> <target-repo> <target-environment>

# With token
./copy-env-vars.sh <token> <source-repo> <source-environment> <target-repo> <target-environment>
```

**Arguments**:

| Position | Name | Description | Required |
|---|---|---|---|
| 1 | `TOKEN` | GitHub personal access token (optional if already logged in) | Conditional |
| 2/1 | `SOURCE_REPO` | Source repository (format: `owner/repo`) | Yes |
| 3/2 | `SOURCE_ENVIRONMENT` | Source environment name | Yes |
| 4/3 | `TARGET_REPO` | Target repository (format: `owner/repo`) | Yes |
| 5/4 | `TARGET_ENVIRONMENT` | Target environment name | Yes |

**Examples**:

```sh
# Already logged in with gh
./copy-env-vars.sh mouralx/home-lab mouras-home-lab mouralx/home-lab staging

# With token
./copy-env-vars.sh ghp_xxxxx mouralx/source-repo production mouralx/target-repo staging
```

**Process**:

1. Verifies `brew` is installed (used to install dependencies)
2. Installs `gh` CLI if not available
3. Installs `jq` if not available
4. Authenticates with `gh` if a token was provided
5. Verifies `gh` is logged in
6. Fetches all variables from the source repo/environment:
   ```sh
   gh variable list --repo "$SOURCE_REPO" --env "$SOURCE_ENV" --json name,value
   ```
7. Fetches all secret names from the source repo/environment:
   ```sh
   gh secret list --repo "$SOURCE_REPO" --env "$SOURCE_ENV" --json name
   ```
8. Copies each variable to the target repo/environment
9. Copies each secret to the target repo/environment with a placeholder value

**Important Limitations**:

- **Secrets cannot be read back** via the GitHub API. The script sets secret values to `REPLACE_WITH_SECRET_VALUE` as a placeholder.
- You must manually update secret values in the target environment after running the script.
- Variables (non-sensitive) are copied with their actual values.

**Use Cases**:
- Setting up a staging environment with identical configuration
- Migrating configuration when restructuring repositories
- Creating a backup of environment configuration
- Bootstrapping a new deployment target
