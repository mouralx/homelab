# Deployment

The home lab is deployed using GitHub Actions with a manually triggered workflow (`workflow_dispatch`). Deployment targets a self-hosted runner on the home lab machine.

## Contents

- [Prerequisites](#prerequisites)
- [Workflow Overview](#workflow-overview)
- [Workflow Inputs](#workflow-inputs)
- [Pipeline Steps](#pipeline-steps)
- [Self-Hosted Runner Setup](#self-hosted-runner-setup)
- [GitHub Environment Configuration](#github-environment-configuration)
- [Adding a New Environment](#adding-a-new-environment)
- [Troubleshooting Deployments](#troubleshooting-deployments)

## Prerequisites

Before deploying, ensure:

1. **Self-hosted runner** is installed and registered on the target machine
2. **GitHub Environment** is configured with required variables and secrets
3. **Docker** and **Docker Compose** are available (the workflow handles installation)
4. **Git** is installed (required for checkout action)
5. The machine has sufficient disk space for the stack (at minimum 10GB for images + configured media allocation)

## Workflow Overview

The workflow is defined in `.github/workflows/publish-home-lab.yaml`.

### Trigger

Only `workflow_dispatch` (manual trigger from GitHub UI or CLI).

### Runner

`runs-on: self-hosted` \u2014 runs locally on the home lab machine.

### Environment

`environment: ${{ inputs.environment }}` \u2014 selected at trigger time, scopes variables and secrets.

## Workflow Inputs

| Input | Type | Options | Default | Description |
|---|---|---|---|---|
| `environment` | Choice | `mouras-home-lab` | `mouras-home-lab` | Target environment for deployment |
| `stack` | Choice | `Everything`, `Agentic`, `Media`, `Automation`, `Identity`, `Data`, `Home`, `Security`, `Monitoring`, `Core Only` | `Everything` | Compose profile to deploy |
| `bring_down_first` | Boolean | \u2014 | `false` | Whether to stop existing stack before deploying |
| `update_images` | Boolean | \u2014 | `false` | Whether to pull latest Docker images (only when `bring_down_first` is true) |

> **Note**: `update_images` only takes effect when `bring_down_first` is also `true`.

### Profile Mappings

| Workflow Option | Compose Profile |
|---|---|
| Everything | (all profiles) |
| Agentic | `--profile agentic` |
| Media | `--profile media` |
| Automation | `--profile automation` |
| Identity | `--profile identity` |
| Data | `--profile data` |
| Home | `--profile home` |
| Security | `--profile security` |
| Monitoring | `--profile monitoring` |
| Core Only | (no profile flags \u2014 npm and postgres only) |

## Pipeline Steps

### 1. Checkout Repository

```yaml
- uses: actions/checkout@v3
```

Checks out the repository to access the compose file, Dockerfiles, and source code.

### 2. Redact Configurations

Injects Transmission credentials into `src/Digger/Digger.Worker/appsettings.json` using `jq`:

```sh
jq \
  --arg transmissionUsername "$TRANSMISSION_USERNAME" \
  --arg transmissionPassword "$TRANSMISSION_PASSWORD" \
  'setpath(["Transmission", "User"]; $transmissionUsername)
   | setpath(["Transmission", "Password"]; $transmissionPassword)' \
  "$appsettings_path" > "$appsettings_tmp"
```

- `TRANSMISSION_USERNAME` comes from `vars` (GitHub Environment variable)
- `TRANSMISSION_PASSWORD` comes from `secrets` (GitHub Environment secret)
- Installs `jq` if not already present

### 3. Set Up Container Runtime

Installs Docker if not already available:

```sh
curl -fsSL https://get.docker.com -o get-docker.sh
sh get-docker.sh
```

### 4. Create .env File

Merges all GitHub Environment variables and secrets into `infra/.env`:

```sh
jq -r 'to_entries[] | select(.key != "GITHUB_TOKEN") | "\(.key)=\(.value | tostring)"' \
  <<< "$ENVIRONMENT_VARS" > infra/.env

jq -r 'to_entries[] | select(.key != "GITHUB_TOKEN") | "\(.key)=\(.value | tostring)"' \
  <<< "$ENVIRONMENT_SECRETS" >> infra/.env
```

- Outputs the `.env` content to workflow logs for verification
- Excludes `GITHUB_TOKEN` from the file

### 5. Bring Down Infrastructure (Optional)

Only runs if `bring_down_first` is `true`:

```sh
docker compose -f infra/compose.yaml down --remove-orphans
```

### 6. Pull New Images (Conditional)

Only runs if both `bring_down_first` AND `update_images` are `true`:

```sh
docker compose -f infra/compose.yaml pull ${{ steps.profiles.outputs.flags }}
```

### 7. Build Infrastructure

Builds all images defined in the compose file (including the custom Dockerfiles for digger, transmission, honcho, and llmster):

```sh
docker compose -f infra/compose.yaml build ${{ steps.profiles.outputs.flags }}
```

### 8. Instantiate Infrastructure

Starts all services in detached mode:

```sh
docker compose -f infra/compose.yaml ${{ steps.profiles.outputs.flags }} up -d
```

### 9. Clean Docker Dangling Images

Removes unused Docker resources to reclaim disk space:

```sh
docker system prune -af
```

## Self-Hosted Runner Setup

### Installation

On the home lab machine:

```sh
# Create a dedicated user
sudo useradd -r -m -d /opt/actions-runner actions-runner
sudo -u actions-runner -s

# Download and configure
mkdir actions-runner && cd actions-runner
curl -o actions-runner-linux-x64-2.xxx.tar.gz -L \
  https://github.com/actions/runner/releases/download/v2.xxx/actions-runner-linux-x64-2.xxx.tar.gz
tar xzf actions-runner-linux-x64-*.tar.gz

# Register (get token from GitHub: Settings > Actions > Runners)
./config.sh --url https://github.com/mouralx/home-lab --token YOUR_TOKEN

# Install as service
sudo ./svc.sh install
sudo ./svc.sh start
```

### Requirements

- **OS**: Linux (tested on Ubuntu)
- **Architecture**: x86_64 (AMD64)
- **Dependencies**: Git, Docker, Docker Compose
- **Disk**: At minimum 20GB for infrastructure + configured media storage
- **RAM**: At minimum 8GB (16GB+ recommended for Hermes + LLMster)

### Verification

```sh
# Check service status
sudo ./svc.sh status

# View runner logs
sudo ./svc.sh check

# Test from GitHub UI
# Actions > Home Lab Deployment > Run workflow
```

## GitHub Environment Configuration

### Required Variables & Secrets

The following items must be configured in the GitHub Environment used by the workflow.

#### Docker Compose Variables

| Name | Type | Description | Required |
|---|---|---|---|
| `DIGGER_HOST_MOVIES_DIR` | Variable | Host path for Digger movie downloads | Yes |
| `HERMES_ALLOWED_USERS` | Variable | Comma-separated usernames allowed to access Hermes | No |
| `HERMES_API_KEY` | Secret | API key for Hermes gateway auth | No |
| `HERMES_DASHBOARD_OIDC_ISSUER` | Variable | OIDC issuer URL for Hermes auth | No |
| `HERMES_DASHBOARD_OIDC_CLIENT_ID` | Variable | OIDC client ID for Hermes auth | No |
| `HERMES_DASHBOARD_PUBLIC_URL` | Variable | Public URL for Hermes dashboard | No |
| `HONCHO_MEMORY_LIMIT` | Variable | Honcho container memory limit | No (default: `2G`) |
| `JELLYFIN_MEDIA_DIR` | Variable | Host path for Jellyfin media library | Yes |
| `JELLYFIN_SERVER_URL` | Variable | Published Jellyfin server URL | Yes |
| `KC_BOOTSTRAP_ADMIN_PASSWORD` | Secret | Keycloak admin password | No (default: `admin`) |
| `KC_BOOTSTRAP_ADMIN_USERNAME` | Variable | Keycloak admin username | No (default: `admin`) |
| `KC_HOSTNAME` | Variable | Keycloak hostname | Yes |
| `KC_HTTP_ENABLED` | Variable | Enable HTTP for Keycloak | No (default: `false`) |
| `KC_PROXY_HEADERS` | Variable | Proxy header mode | No (default: `xforwarded`) |
| `LLM_MODEL` | Variable | LLMster model to load | No (default: `google/gemma-4-e2b`) |
| `N8N_ENCRYPTION_KEY` | Secret | n8n encryption key | Yes |
| `N8N_HOST` | Variable | n8n hostname | Yes |
| `OPENCODE_API_KEY` | Secret | API key for Hermes and Honcho | Yes |
| `PGADMIN_DEFAULT_EMAIL` | Variable | pgAdmin login email | Yes |
| `PGADMIN_DEFAULT_PASSWORD` | Secret | pgAdmin login password | Yes |
| `POSTGRES_PASSWORD` | Secret | Postgres superuser password | Yes |
| `POSTGRES_USER` | Variable | Postgres superuser username | Yes |
| `TIME_ZONE` | Variable | Container timezone (e.g., `Europe/Lisbon`) | Yes |
| `TRANSMISSION_DOWNLOADS_DIR` | Variable | Host path for completed downloads | Yes |
| `TRANSMISSION_INCOMPLETE_DIR` | Variable | Host path for incomplete downloads | Yes |
| `TRANSMISSION_USERNAME` | Variable | Transmission RPC username | Yes |
| `TRANSMISSION_PASSWORD` | Secret | Transmission RPC password | Yes |

### Environment Configuration Steps

1. Go to GitHub repo: **Settings > Environments > New environment**
2. Name the environment (match workflow choices: `mouras-home-lab`)
3. Add environment variables (non-sensitive values)
4. Add environment secrets (passwords, tokens, API keys)
5. Set **required reviewers** if desired (prevent accidental deployment)
6. Set **deployment branches** if desired (optional)

## Adding a New Environment

To deploy to a different machine or configuration:

1. **Create a new GitHub Environment** with the required variables and secrets
2. **Register a new self-hosted runner** on the target machine
3. **Update the workflow** to include the new environment in the input options:

```yaml
inputs:
  environment:
    options:
      - mouras-home-lab
      - new-environment-name  # Add this
```

4. **Adjust compose paths** in the new environment's variables to match the target machine's filesystem

## Troubleshooting Deployments

### Deployment Fails at Checkout

```
fatal: could not read Username for 'https://github.com'
```

**Cause**: Self-hosted runner may not have network access or git credentials.
**Fix**: Ensure the runner has outbound HTTPS access to `github.com`.

### .env File Missing Variables

```
WARNING: The POSTGRES_PASSWORD variable is not set. Defaulting to a blank string.
```

**Cause**: Environment is missing required variables or secrets.
**Fix**: Verify all required entries are configured in the GitHub Environment settings.

### Compose Build Fails

```
ERROR: build failed: failed to solve: ... not found
```

**Cause**: Missing source files or network issues when pulling base images.
**Fix**:
- Ensure the repository checkout is complete
- Check Docker Hub/GHCR availability
- For custom images, verify Dockerfile paths

### Containers Start but Crash

**Symptoms**: Containers restart in a loop or exit immediately.

**Common Causes**:

| Service | Common Issue | Check |
|---|---|---|
| Postgres | Data directory permissions | `ls -la ~/postgres/data` |
| Keycloak | Database not initialized | Keycloak logs, Postgres logs |
| Digger | appsettings.json not found | Check volume mounts |
| Transmission | Port already in use | `lsof -i :9091` |
| Jellyfin | Media directory permissions | `ls -la /mnt/ssd/` |
| Honcho | Postgres not ready or pgvector missing | Honcho logs, Postgres logs |

### Runner Disk Full

The `docker system prune` step helps, but large media files can still fill the disk.

**Recovery**:
```sh
# Check disk usage
df -h

# Find large files
du -sh /* 2>/dev/null | sort -rh | head -10

# Clean Docker aggressively
docker system prune -af --volumes

# Remove old media files manually
rm -rf /mnt/ssd/transmission/downloads/*
```
