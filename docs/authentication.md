# Authentication options

No centralized identity provider is deployed in this stack. Applications retain their existing authentication settings. The following integrations are options if an external identity provider is added later; none are enabled by the current Compose configuration. Database credentials remain separate from user login.

## Service compatibility

| Service | Integration path | Requirements and limits |
| --- | --- | --- |
| Portainer | Custom OAuth | Configure the identity-provider client and endpoints under Settings → Authentication → OAuth → Custom. [Documentation](https://docs.portainer.io/admin/settings/authentication/oauth) |
| n8n | Built-in OIDC or SAML | Self-hosted SSO requires a Business or Enterprise license; using the container image alone does not enable this feature. [Documentation](https://docs.n8n.io/deploy/host-n8n/configure-n8n/security/configure-sso) |
| Jellyfin | Third-party OIDC/SAML plugin | Requires a compatible plugin; browser SSO may not work in every TV/mobile client. The original 9p4 plugin is archived; evaluate a maintained fork against the installed Jellyfin version. [Example plugin](https://github.com/Flowfin/jellyfin-plugin-sso) |
| OpenClaw | OIDC-connected identity proxy plus trusted-proxy authentication | Requires an additional proxy such as OAuth2 Proxy and OpenClaw trusted-proxy configuration. [Documentation](https://docs.openclaw.ai/gateway/trusted-proxy) |
| Home Assistant | Custom authentication integration or workaround | No built-in OIDC authentication provider. Protecting the web UI with a proxy does not by itself replace Home Assistant login. [Supported providers](https://www.home-assistant.io/docs/authentication/providers/) |
| Nginx Proxy Manager | Authentication proxy in front of the admin UI | No native SSO integration configured. This adds an access gate; NPM's own admin login remains. |
| Transmission | Authentication proxy in front of the web UI | No native SSO integration configured. Browser redirects may interfere with RPC clients; plan RPC access separately. |

## Setup approach

If you later add an identity provider such as Keycloak, use an externally reachable HTTPS hostname. Both browsers and application containers must be able to resolve and reach the issuer URL. Use its realm issuer, such as `https://auth.example.com/realms/homelab`, rather than an internal container address for browser-facing SSO.

Create a separate client for each application, with its exact HTTPS callback URL. Configure client secrets, scopes, and role/group mappings according to each application's documentation. Start with Portainer; configure Jellyfin after checking plugin and client compatibility. Enable n8n SSO only with an appropriate license.

For OpenClaw trusted-proxy authentication, restrict trusted proxy addresses, ensure the proxy replaces identity headers, and prevent direct access that could bypass the proxy. Adding an authentication proxy requires a new service and routing configuration; the current Compose stack contains none.

Retain a local administrator recovery path while testing SSO. Verify login, logout, account provisioning, application permissions, and the clients you actually use before relying on centralized login.

## GitHub deployment

The existing workflow generates `.env` from all variables and secrets in the selected GitHub environment, so no explicit per-variable mapping is needed. No identity-provider client secrets are currently consumed by Compose. Add secrets to that environment only when their corresponding application configuration is implemented.

The workflow deploys repository configuration, but it does not provision an identity provider or its clients, configure Portainer through its UI, install Jellyfin plugins, or edit Home Assistant's host configuration. These actions remain separate from container deployment.
