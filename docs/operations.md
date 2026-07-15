# Operations

Day-to-day operations, maintenance procedures, backup strategies, and troubleshooting for the home lab.

## Contents

- [Daily Operations](#daily-operations)
- [Monitoring](#monitoring)
- [Backup Strategy](#backup-strategy)
- [Maintenance Procedures](#maintenance-procedures)
- [Update Procedures](#update-procedures)
- [Troubleshooting](#troubleshooting)
- [Reset and Recovery](#reset-and-recovery)
- [Performance Tuning](#performance-tuning)

## Daily Operations

### Checking Service Health

```sh
# List running services
docker compose --env-file infra/.env -f infra/compose.yaml ps

# Check resource usage
docker stats --no-stream

# Quick health check (all services)
docker compose --env-file infra/.env -f infra/compose.yaml ps --services --filter "status=running"

# Check all containers including stopped
docker ps -a
```

### Viewing Logs

```sh
# All services
docker compose --env-file infra/.env -f infra/compose.yaml logs

# Specific service (follow)
docker compose --env-file infra/.env -f infra/compose.yaml logs -f digger

# Last N lines
docker compose --env-file infra/.env -f infra/compose.yaml logs --tail=50 n8n

# Timestamps
docker compose --env-file infra/.env -f infra/compose.yaml logs -t

# Specific time range
docker logs --since=2026-07-15T10:00:00 --until=2026-07-15T11:00:00 digger
```

### Quick Health Commands

```sh
# Check Digger status
docker exec digger ls -la /digger/data/

# Check Transmission RPC
docker exec transmission curl -s http://localhost:9091/transmission/rpc \
  -d '{"method":"session-stats"}'

# Check Postgres connectivity
docker exec postgres pg_isready -U postgres

# Check disk usage
df -h /mnt/ssd /home

# Check media storage
du -sh /mnt/ssd/transmission/downloads/
```

## Monitoring

### Key Metrics to Watch

| Metric | Where to Check | Warning Threshold | Critical Threshold |
|---|---|---|---|
| Disk usage `/` | `df -h /` | >80% | >90% |
| Disk usage `/mnt/ssd` | `df -h /mnt/ssd` | >80% | >90% |
| Memory usage | `free -h` or Docker stats | >80% | >90% |
| Digger cycle status | `docker logs digger` | Warnings in log | Errors in log |
| Transmission queue | Digger logs or Transmission UI | >5 active | >10 active |
| Postgres connections | `docker exec postgres psql -c "SELECT count(*) FROM pg_stat_activity;"` | >20 | >50 |
| Jellyfin uptime | `docker inspect jellyfin -f '{{.State.Status}}'` | Not running | Restart loop |

### Digger-Specific Monitoring

```sh
# Check movie database statistics
docker exec digger sqlite3 /digger/data/movies.db \
  "SELECT LastKnownStatus, COUNT(*) as Count FROM Movies GROUP BY LastKnownStatus ORDER BY Count DESC;"

# Check total managed space
docker exec digger sqlite3 /digger/data/movies.db \
  "SELECT LastKnownStatus, COUNT(*) as Count, SUM(Size) as TotalBytes FROM Movies GROUP BY LastKnownStatus;"

# Recent activity
docker exec digger sqlite3 /digger/data/movies.db \
  "SELECT Name, LastKnownStatus, Timestamp FROM Movies ORDER BY Timestamp DESC LIMIT 10;"

# Failed movies
docker exec digger sqlite3 /digger/data/movies.db \
  "SELECT Name, DownloadAttempt, Timestamp FROM Movies WHERE LastKnownStatus = 'Failed';"

# Skipped movies
docker exec digger sqlite3 /digger/data/movies.db \
  "SELECT Name, Size, PublishDate FROM Movies WHERE LastKnownStatus = 'Skipped' ORDER BY PublishDate DESC;"
```

### Automated Monitoring Setup

Consider adding a health check endpoint or setting up Uptime Kuma as an additional service to monitor:

- HTTP endpoint checks for NPM, Jellyfin, n8n
- Docker container status verification
- Disk usage alerts
- Certificate expiry monitoring (NPM handles this internally, but additional monitoring is useful)

## Backup Strategy

### What to Back Up

| Data | Priority | Frequency | Method | Size Estimate |
|---|---|---|---|---|
| Postgres databases | Critical | Daily | `pg_dump` | Small |
| NPM config + certs | Critical | Weekly | File copy | Small |
| Home Assistant config | Important | Weekly | File copy | Small |
| Jellyfin config | Important | Weekly | File copy | Small |
| Digger SQLite DB | Important | Weekly | File copy | Small |
| Postgres data directory | Moderate | Weekly | File copy | Medium |
| Media files | Low | On-demand | Re-downloadable | Large |

### Backup Scripts

#### Postgres Backup

```sh
#!/bin/bash
BACKUP_DIR="/backups/postgres"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
mkdir -p "$BACKUP_DIR"

docker exec postgres pg_dumpall -U postgres > "$BACKUP_DIR/full_backup_$TIMESTAMP.sql"

# Compress
gzip "$BACKUP_DIR/full_backup_$TIMESTAMP.sql"

# Keep only last 7 days
find "$BACKUP_DIR" -name "*.sql.gz" -mtime +7 -delete
```

#### Configuration Backup

```sh
#!/bin/bash
BACKUP_DIR="/backups/config"
TIMESTAMP=$(date +%Y%m%d)
mkdir -p "$BACKUP_DIR"

tar -czf "$BACKUP_DIR/npm_$TIMESTAMP.tar.gz" ~/npm/
tar -czf "$BACKUP_DIR/homeassistant_$TIMESTAMP.tar.gz" ~/homeassistant/config/
tar -czf "$BACKUP_DIR/jellyfin-config_$TIMESTAMP.tar.gz" ~/jellyfin/config/
tar -czf "$BACKUP_DIR/digger_$TIMESTAMP.tar.gz" ~/digger/

# Keep only last 30 days
find "$BACKUP_DIR" -name "*.tar.gz" -mtime +30 -delete
```

#### Full Compose Backup (Before Major Changes)

```sh
#!/bin/bash
BACKUP_DIR="/backups/pre-update"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
mkdir -p "$BACKUP_DIR"

# Dump databases
docker exec postgres pg_dumpall -U postgres > "$BACKUP_DIR/postgres_$TIMESTAMP.sql"

# Copy all state directories
for dir in digger postgres n8n jellyfin homeassistant npm portainer vault hermes keycloak; do
    if [ -d ~/$dir ]; then
        cp -r ~/$dir "$BACKUP_DIR/$dir/"
    fi
done

# Compress everything
tar -czf "$BACKUP_DIR/../pre_update_$TIMESTAMP.tar.gz" -C "$BACKUP_DIR" .
rm -rf "$BACKUP_DIR"
```

### Restoration Procedures

#### Postgres

```sh
# Stop services that depend on Postgres
docker compose -f infra/compose.yaml stop n8n keycloak pgadmin

# Restore
gunzip -c /backups/postgres/full_backup_20260715_120000.sql.gz | \
  docker exec -i postgres psql -U postgres

# Restart services
docker compose -f infra/compose.yaml start n8n keycloak pgadmin
```

#### Digger Database

```sh
# Stop Digger
docker compose -f infra/compose.yaml stop digger

# Restore
cp /backups/config/digger_20260715/movies.db ~/digger/movies.db

# Start Digger
docker compose -f infra/compose.yaml start digger
```

#### Full Stack Recovery

```sh
# Bring down everything
docker compose --env-file infra/.env -f infra/compose.yaml down

# Restore configuration directories
tar -xzf /backups/pre_update_20260715.tar.gz -C /

# Restart
docker compose --env-file infra/.env -f infra/compose.yaml up -d
```

### Backup Storage Recommendations

- **Local**: Keep backups on a separate disk or partition
- **Remote**: Consider rsync to a NAS or cloud storage
- **Retention**: 7 daily, 4 weekly, 3 monthly
- **Testing**: Verify backup integrity monthly by attempting a restore to a test directory

## Maintenance Procedures

### Regular Maintenance Schedule

| Frequency | Task |
|---|---|
| Daily | Check service health, review Digger logs |
| Weekly | Review disk usage, check for failed Digger downloads |
| Monthly | Apply OS updates on host, update Docker images, review backup integrity |
| Quarterly | Rotate secrets, review compose configuration, prune unused Docker volumes |
| Annually | Audit service versions, plan upgrades for end-of-life images |

### Monthly Maintenance

#### 1. Update Docker Images

```sh
# Via GitHub Actions (recommended)
# - Trigger workflow with bring_down_first=true, update_images=true

# Or manually:
docker compose --env-file infra/.env -f infra/compose.yaml pull
docker compose --env-file infra/.env -f infra/compose.yaml up -d
docker system prune -af
```

#### 2. Check for Container Updates

```sh
# List images that have updates available
docker compose --env-file infra/.env -f infra/compose.yaml images
docker compose --env-file infra/.env -f infra/compose.yaml pull --dry-run
```

#### 3. Review and Rotate Logs

```sh
# Check Docker log sizes
docker ps -q | xargs -I {} sh -c 'echo "{}: $(docker inspect {} -f {{.LogPath}} | xargs ls -lh 2>/dev/null | awk "{print \$5}")"'

# Limit log size (add to daemon.json)
# /etc/docker/daemon.json
# { "log-driver": "json-file", "log-opts": { "max-size": "10m", "max-file": "3" } }
```

### Quarterly Maintenance

1. **Rotate all secrets** in GitHub Environment settings
2. **Review and prune Digger database**:

```sh
# Check database size
docker exec digger sqlite3 /digger/data/movies.db "PRAGMA page_count; PRAGMA page_size;"

# Vacuum to reclaim space
docker exec digger sqlite3 /digger/data/movies.db "VACUUM;"

# Review old/skipped movies
docker exec digger sqlite3 /digger/data/movies.db \
  "SELECT COUNT(*) FROM Movies WHERE LastKnownStatus = 'Skipped' AND Timestamp < datetime('now', '-30 days');"
```

3. **Prune unused Docker resources**:

```sh
docker system prune -af --volumes
docker builder prune -af
```

## Update Procedures

### Updating the Digger Worker

1. Make changes to the Digger source code
2. Commit and push to GitHub
3. Trigger deployment workflow from GitHub Actions
4. The new Docker image will be built from source

### Updating Third-Party Images

1. Option A (pull during deploy): Trigger workflow with `bring_down_first=true` and `update_images=true`
2. Option B (manual): Pull images, then deploy without downtime:

```sh
# Pull new images
docker compose --env-file infra/.env -f infra/compose.yaml pull

# Recreate containers with new images
docker compose --env-file infra/.env -f infra/compose.yaml up -d --force-recreate
```

### Rolling Back

1. Revert the code changes in GitHub
2. Trigger the deployment workflow
3. If the database schema changed, restore the Digger SQLite database from backup
4. For Postgres-backed services, restore from the latest database backup

## Troubleshooting

### Service Won't Start

**Proceed in order**:

```sh
# 1. Check logs
docker compose logs <service-name>

# 2. Check if ports are available
lsof -i :<port-number>

# 3. Check volume permissions
ls -la ~/<service-directory>

# 4. Try starting interactively
docker compose run --rm <service-name>

# 5. Rebuild (for custom images)
docker compose build <service-name>
docker compose up -d <service-name>
```

### Disk Full Scenarios

```sh
# Find what's using space
du -sh /* 2>/dev/null | sort -rh | head -20

# Check Docker disk usage
docker system df

# Prune aggressively
docker system prune -af --volumes

# Remove old media
rm -rf /mnt/ssd/transmission/downloads/*unwanted*

# Clean Jellyfin cache
rm -rf ~/jellyfin/cache/*
docker compose restart jellyfin
```

### Digger-Specific Issues

| Symptom | Diagnosis Command | Likely Cause | Resolution |
|---|---|---|---|
| Worker won't start | `docker logs digger` | SQLite file corrupt | Restore from backup |
| No new movies found | `docker logs digger --tail=50` | YTS API unreachable | Check network/DNS |
| Downloads stuck | `docker exec digger sqlite3 /digger/data/movies.db "SELECT Name, LastKnownStatus FROM Movies WHERE LastKnownStatus IN ('NotEnqueued', 'Enqueued');"` | Transmission unreachable | Check Transmission container |
| Space exceeded | `df -h /mnt/ssd` | Media too large | Increase MaxAllocatedSpace or clear media |

### Common Error Messages

```
Error: Cannot connect to the Docker daemon
```
Docker service is not running.
```sh
sudo systemctl start docker
```

```
transmission-daemon: Error loading config file
```
Transmission config file is corrupt or has permission issues.
```sh
docker compose -f infra/compose.yaml down transmission
rm -rf ~/transmission/config
docker compose -f infra/compose.yaml up -d transmission
```

```
An item with the same key has already been added. Key: X
```
Duplicate key in appsettings.json or .env file. Check for duplicate variable definitions.

```
Digger: Error occurred during CleanAndSyncMovies operation
```
Transmission not responding or insufficient permissions on download directory.
Check Transmission is running and verify directory permissions.

## Reset and Recovery

### Reset a Single Service

```sh
# Stop and remove the container (data persists in volumes)
docker compose -f infra/compose.yaml rm -fs <service-name>
docker compose -f infra/compose.yaml up -d <service-name>
```

### Factory Reset a Service (Destroy Data)

```sh
# Remove data and recreate
docker compose -f infra/compose.yaml rm -fs <service-name>
rm -rf ~/<service-name>/*
docker compose -f infra/compose.yaml up -d <service-name>
```

### Full Stack Reset

```sh
# Backup first!
./scripts/backup.sh

# Bring everything down
docker compose --env-file infra/.env -f infra/compose.yaml down

# Remove all containers, networks, and volumes
docker compose --env-file infra/.env -f infra/compose.yaml down -v

# Prune everything
docker system prune -af --volumes

# Rebuild and restart
docker compose --env-file infra/.env -f infra/compose.yaml up -d --build
```

### Emergency Recovery

If the machine becomes unresponsive or services won't start:

```sh
# 1. Hard reset of Docker
sudo systemctl restart docker

# 2. If Docker won't start, check daemon logs
sudo journalctl -u docker.service --no-pager | tail -50

# 3. Last resort: purge and reinstall Docker
sudo apt-get purge -y docker-ce docker-ce-cli containerd.io
sudo rm -rf /var/lib/docker
curl -fsSL https://get.docker.com | sh
# Then re-deploy via GitHub Actions
```

## Performance Tuning

### Resource Limits

The compose file already defines resource limits for Hermes (4GB RAM, 2 CPUs). Consider adding limits to other services:

```yaml
services:
  digger:
    deploy:
      resources:
        limits:
          memory: 512M
          cpus: "1.0"
```

### SQLite Performance

The Digger SQLite database benefits from occasional maintenance:

```sh
# Run weekly
docker exec digger sqlite3 /digger/data/movies.db "PRAGMA optimize;"

# Run monthly
docker exec digger sqlite3 /digger/data/movies.db "VACUUM;"
docker exec digger sqlite3 /digger/data/movies.db "REINDEX;"
```

### Docker Performance

```sh
# Limit log file sizes
sudo tee /etc/docker/daemon.json <<EOF
{
  "log-driver": "json-file",
  "log-opts": {
    "max-size": "10m",
    "max-file": "3"
  },
  "storage-driver": "overlay2"
}
EOF
sudo systemctl restart docker
```
