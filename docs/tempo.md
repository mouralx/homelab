# Tempo

Tempo is defined in the existing `lab` project. The addresses, paths and ports below are defined in Compose. No local Tempo data or users are copied. Homelab pulls Tempo's own published images; it does not build or check out Tempo source.

- Portal: `https://tempo.mouras.me`, container `10.203.0.26:8080`.
- Identity: `https://keycloak.mouras.me`, container `keycloak` at `10.203.0.27:8080`.
- Images: `ghcr.io/mouralx/tempo/api:${TEMPO_TAG:-stable}` and `ghcr.io/mouralx/tempo/portal:${TEMPO_TAG:-stable}`.
- Container `postgres` runs PostgreSQL 17 and holds separate application and Keycloak databases under `~/postgres/data`. Host mappings: portal `8080`, Keycloak `8081`, API `8082`, PostgreSQL `127.0.0.1:5433`.
- Compose service names are `postgres` and `keycloak`. Both join `homelab_static` with all other services and retain the DNS aliases `tempo-postgres` and `tempo-keycloak` for existing application URLs. The API also uses this shared network.
- Database migration is an explicit deployment step using `docker compose run --rm --no-deps -T tempo-api --migrate`. There is no separate migration service.

## Registry access and publication

In the `mouras-home-lab` GitHub environment, configure `GH_PAT` with package-read permission and access to the private Tempo packages; configure `GH_USER` for the PAT owner if different from the workflow actor. The deployment workflow uses this PAT to log into GHCR. The homelab's default GitHub token does not substitute for private cross-repository access. Do not store the PAT in Compose, source files or image URLs.

Tempo's publication workflow must publish the API and portal images before initial installation. Its main branch publishes `vMAJOR.MINOR.PATCH`, `stable`, `latest` and SHA tags; develop publishes `prev-vMAJOR.MINOR.PATCH`, `preview` and preview SHA tags. Set `TEMPO_TAG` to a shared explicit version for reproducible deployment, or `stable` to follow stable publications. Publication only updates affected components, so channel aliases may point to different version numbers; an explicit version must exist for both images before pinning both to it.

## Credentials

Configure four required secrets in the `mouras-home-lab` GitHub environment: `TEMPO_POSTGRES_PASSWORD`, `TEMPO_APP_DB_PASSWORD`, `TEMPO_IDENTITY_DB_PASSWORD` and `TEMPO_KEYCLOAK_ADMIN_PASSWORD`. Choose independent passwords with at least 16 characters containing only letters, digits, `_` or `-`. The workflow validates their format without printing them. Passwords are no longer generated or persisted by a repository script. For manual Compose commands, supply the same values through your protected `.env` file (start with [`.env.example`](../.env.example)) or shell environment.

If the previous credential-generation script was used, before the first deployment after this change copy the existing values from Beelink's `~/.config/tempo/credentials.json` into the matching GitHub environment secrets using a private session. Do not generate replacement values for an initialized installation: changing environment variables does not change existing database role passwords or the existing Keycloak administrator password. Keep the old credential file as a protected backup until the transition has been verified; the workflow no longer reads it. Password rotation requires updating the actual credentials and their deployment secrets together.

Database initialization lives in `services/tempo/init.sh`, mounted read-only into PostgreSQL’s `/docker-entrypoint-initdb.d`. Keycloak imports the read-only `services/tempo/tempo-realm.json` file from `/opt/keycloak/data/import`, resolving `TEMPO_PORTAL_URL` itself. Initialization runs only on an empty PostgreSQL data directory; realm import skips an existing realm. Compose mounts these files directly from the repository checkout, so separate copies under `~/tempo` are not required. Missing configuration files cause container creation to fail instead of silently creating directories. Do not delete persistent storage to change configuration.

## Deploy

Run **Deploy Home Lab** with `action=install`, first with `dry_run=true`, then with `dry_run=false`. Enable `update_images` to pull image updates through the PAT. It uses the existing Beelink runner and deploys the full stack, including Tempo. Set the GitHub environment variable `TEMPO_TAG` to choose the Tempo image version. All Tempo services are defined directly in `services/compose.yaml`.

The deployment workflow performs no DNS, proxy-manager or account changes. PostgreSQL has a TCP readiness health check; other container health checks are disabled, and deployment does not wait for application readiness. The workflow calls `.github/scripts/migrate-tempo.sh`, which stops the Tempo portal and API, starts PostgreSQL, waits up to two minutes for PostgreSQL to become healthy, verifies authenticated TCP access to both databases, and runs the selected API image once with `--migrate`. The temporary container is removed after execution. Only a successful migration allows the workflow to start the stack; failure leaves Tempo stopped for diagnosis. Normal API startup does not apply migrations. Check service logs and verify external login after deployment. Published HTTP ports do not change the configured HTTPS portal and identity URLs; use those URLs for sign-in. The Keycloak host port exposes its HTTP interface, including administrative routes, to the LAN; keep public traffic routed through Nginx Proxy Manager's restricted HTTPS configuration below.

## HTTPS routing

Create DNS records using the same ingress as the other homelab domains. In Nginx Proxy Manager create two proxy hosts with TLS, forced HTTPS and correct forwarded headers:

| Domain | Forward scheme | Forward address | Port |
| --- | --- | --- | --- |
| `tempo.mouras.me` | http | `10.203.0.26` | 8080 |
| `keycloak.mouras.me` | http | `10.203.0.27` | 8080 |

Keycloak trusts forwarded headers only from NPM's reserved address `10.203.0.12`. Keep Keycloak `/admin`, `/realms/master`, `/health` and `/metrics` inaccessible through the public proxy; use a restricted administrative route/tunnel for account provisioning. Do not put an additional interactive login in front of the OIDC endpoints. The imported realm configures portal redirects for `TEMPO_PORTAL_URL`, defaulting to `https://tempo.mouras.me`.

This provisions a dedicated Keycloak instance for Tempo at the requested domain. Do not point the domain at it if that name already serves another identity provider; instead add the Tempo realm/client to the existing instance and adjust this stack first.

## First login and verification

Use restricted Keycloak administration to provision your account in the `tempo` realm, configure SMTP and verify the email. The first portal user becomes the installation owner, so provision and sign in as the intended owner before granting other users access. Invite subsequent members through project teams.

After deployment verify portal HTTPS, issuer `https://keycloak.mouras.me/realms/tempo`, login/logout, timer start/stop, project invitations and PDF reports. Record the deployed image digests. No public DNS, proxy configuration, live account login or Beelink deployment is claimed verified by the local configuration tests.

References: [PostgreSQL initialization scripts](https://hub.docker.com/_/postgres), [Keycloak realm import](https://www.keycloak.org/server/importExport).

## Schema updates and manual installation

Before schema-changing releases, take and verify a database backup and retain the previous image versions. Use the same `TEMPO_TAG` and credentials for migration and API startup. The workflow does not take a backup automatically. For manual deployment, run from the repository root with a protected `.env`:

```bash
docker compose --env-file .env -f services/compose.yaml stop tempo-portal tempo-api
docker compose --env-file .env -f services/compose.yaml up -d --wait --wait-timeout 120 postgres
# Verify both database logins; stop here if either fails:
docker compose --env-file .env -f services/compose.yaml exec -T postgres sh -ec '
  export PGCONNECT_TIMEOUT=5
  PGPASSWORD="$APP_DB_PASSWORD" psql -X -w -h tempo-postgres -p 5432 -U tempo -d tempo -v ON_ERROR_STOP=1 -c "SELECT 1" >/dev/null
  PGPASSWORD="$IDENTITY_DB_PASSWORD" psql -X -w -h tempo-postgres -p 5432 -U keycloak -d keycloak -v ON_ERROR_STOP=1 -c "SELECT 1" >/dev/null
'
docker compose --env-file .env -f services/compose.yaml run --rm --no-deps -T tempo-api --migrate
# Run only after migration succeeds:
docker compose --env-file .env -f services/compose.yaml up -d
```

A plain `up -d` does not apply schema updates. If migrating from the previous Compose definition, remove the stopped `tempo-migrate` container after verifying that no migration is running (`docker rm tempo-migrate`); preserve PostgreSQL data. For deployment-controlled migration guidance, see [EF Core migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying).

## PostgreSQL access

| Client | Address inside Docker | Database | Role | Deployment secret |
| --- | --- | --- | --- | --- |
| Tempo API and one-off migration | `tempo-postgres:5432` | `tempo` | `tempo` | `TEMPO_APP_DB_PASSWORD` |
| Keycloak | `tempo-postgres:5432` | `keycloak` | `keycloak` | `TEMPO_IDENTITY_DB_PASSWORD` |

These services share `lab_net`; `tempo-postgres` is PostgreSQL’s network alias. Both normal API startup and Keycloak wait for PostgreSQL’s health check. The workflow waits explicitly before the one-off migration because `run --no-deps` bypasses dependencies. The host mapping `127.0.0.1:5433` is only for host administration; containers use port `5432`.

Initialization creates separate database owners and revokes public access to both databases. Other stack services have no configured PostgreSQL dependency: Digger uses SQLite, Vault uses Raft, and the portal talks to the API. Existing Gitea/Home Assistant settings live in their persistent configuration and must be inspected on the host if they have been customized to use PostgreSQL. The shared network permits TCP reachability; database authentication controls access. The deployment login checks detect missing roles/databases or mismatched passwords but do not repair an existing database.
