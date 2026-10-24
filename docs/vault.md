# Vault

Vault runs as a single server with persistent Raft storage, using the [official configuration guidance](https://developer.hashicorp.com/vault/docs/configuration). This provides persistence but no high availability. Memory locking is disabled for Raft; disable swap on the host or use encrypted swap.

## Start and configure HTTPS

From the repository root, using the stack's existing `.env`:

```bash
docker compose --env-file .env -f services/compose.yaml up -d vault
```

In Nginx Proxy Manager, create a proxy host for `vault.mouras.me`, forwarding HTTP to `vault:8200`. Configure a valid certificate and force HTTPS. Point DNS at the same ingress as your other HTTPS services. If you choose another domain, update `VAULT_API_ADDR` in Compose to match it.

TLS terminates at the proxy. Traffic from the proxy to Vault crosses the shared Docker bridge over HTTP, so services on that bridge must be trusted. The host listener is bound to localhost only. For direct administrative access, forward it over SSH:

```bash
ssh -L 8200:127.0.0.1:8200 <user>@<beelink-address>
```

Then open `http://localhost:8200/ui/` locally, or use `https://vault.mouras.me/ui/` after configuring the proxy.

## Initialize and unseal

Initialize once using the UI or run on Beelink:

```bash
docker compose --env-file .env -f services/compose.yaml exec vault vault operator init
```

The default initialization generates five unseal key shares and requires three to unseal. Store the shares and initial root token securely outside this repository and outside Vault. Do not put them in Compose, CI logs, or `.env`.

Run the following command three times, supplying a different share at each hidden prompt:

```bash
docker compose --env-file .env -f services/compose.yaml exec vault vault operator unseal
```

Sign in with the initial root token to configure authentication, policies, and secrets engines. Create scoped access for normal use and revoke the initial root token after setup. No development token or automatic initialization is configured.

Vault seals again after every restart and needs three shares to unseal. The Compose health check accepts sealed and uninitialized responses so the full-stack deployment can finish before manual setup. It checks HTTP availability, not readiness to serve secrets. Check the actual seal state with:

```bash
docker compose --env-file .env -f services/compose.yaml exec vault vault status
```

## Backups

After authenticating in an interactive container shell with a token authorized to take snapshots, save a Raft snapshot and copy it off the container:

```bash
docker compose --env-file .env -f services/compose.yaml exec vault sh
vault login
vault operator raft snapshot save /tmp/vault.snap
exit
docker compose --env-file .env -f services/compose.yaml cp vault:/tmp/vault.snap ./vault.snap
```

Keep snapshots in protected off-host storage, outside the repository, and retain the unseal shares separately. Regularly test restoration using the [Raft snapshot restore procedure](https://developer.hashicorp.com/vault/docs/commands/operator/raft). `docker compose down` retains the volume; `down -v` deletes it.
