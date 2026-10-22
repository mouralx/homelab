# Tempo

Tempo is a fresh installation in the existing `lab` project. No local Tempo data or users are copied. Homelab pulls Tempo's own published images; it does not build or check out Tempo source.

- Portal: `https://tempo.mouras.me`, container `10.203.0.26:8080`.
- Identity: `https://keycloak.mouras.me`, container `10.203.0.27:8080`.
- Images: `ghcr.io/mouralx/tempo/api:${TEMPO_TAG:-stable}` and `ghcr.io/mouralx/tempo/portal:${TEMPO_TAG:-stable}`.
- Private PostgreSQL 17 holds separate application and Keycloak databases in `lab_tempo_postgres_data`. No database or API host ports are published.
- The migration service uses the same API image and completes before API startup.

## Registry access and publication

In the `mouras-home-lab` GitHub environment, configure `GH_PAT` with package-read permission and access to the private Tempo packages; configure `GH_USER` for the PAT owner if different from the workflow actor. Both deployment workflows use this PAT to log into GHCR. The homelab's default GitHub token does not substitute for private cross-repository access. Do not store the PAT in Compose, source files or image URLs.

Tempo's publication workflow must publish the API and portal images before initial installation. Its main branch publishes `vMAJOR.MINOR.PATCH`, `stable`, `latest` and SHA tags; develop publishes `prev-vMAJOR.MINOR.PATCH`, `preview` and preview SHA tags. Set `TEMPO_TAG` to a shared explicit version for reproducible deployment, or `stable` to follow stable publications. Publication only updates affected components, so channel aliases may point to different version numbers; an explicit version must exist for both images before pinning both to it.

## Credentials

The workflow prepares four independent passwords in `~/.config/tempo/credentials.json` on Beelink, mode 0600 inside a 0700 directory. Optional GitHub environment secrets `TEMPO_POSTGRES_PASSWORD`, `TEMPO_APP_DB_PASSWORD`, `TEMPO_IDENTITY_DB_PASSWORD` and `TEMPO_KEYCLOAK_ADMIN_PASSWORD` supply initial values. Use at least 16 characters containing letters, digits, `_` or `-`. Otherwise random 256-bit hex values are generated on first deployment. Values are masked before being exported to later workflow steps.

Back up this file with the database backups. Subsequent deployments reuse it; changing a secret without changing the database credentials is rejected. Password rotation requires changing database roles/Keycloak credentials and the stored file together. PostgreSQL initialization and Keycloak realm import run only on fresh databases. Do not delete persistent storage to change configuration.

## Deploy

Run **Deploy Tempo** on main, first with `dry_run=true`, then with `dry_run=false`. It uses the existing Beelink runner and deployment concurrency group, pulls the images through the PAT, and starts only Tempo and its dependencies. The full **Deploy Home Lab** workflow also includes Tempo. Compose with `include` support (2.20+) is required.

The focused workflow intentionally performs no DNS, proxy-manager or account changes. It publishes no host ports and leaves existing service containers alone. A successful container health check does not prove external login works.

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
