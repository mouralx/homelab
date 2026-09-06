# RPi5 automatic movie library

## Behaviour

Popular/recent discovery -> automatic Radarr addition and search -> Prowlarr/Knaben -> Transmission -> organized Movies -> Jellyfin.

No manual movie additions are required. `scripts/media-cleanup/replenish.py` reads Radarr's TMDb popular/trending discovery, retaining provider order, excludes every existing/excluded TMDb ID (including retired unmonitored movies), and selects recent movies with an already-passed digital or physical release date. Recent means a cinema release within 730 days; absent that date, the current or preceding calendar year.

The `media-cleanup` Portainer stack runs cleanup followed by replenishment every 15 minutes. Replenishment adds at most one movie per six hours and allows at most two monitored movies without files. Movies are tagged `auto-popular-recent` (live tag ID 1). Initial deployment selected The Whisper Man automatically and verified an actual 2160p WEB-DL transfer in Transmission, not just a submitted search.

## Space and retirement

- Existing cleanup policy is unchanged: below 15% actual media-filesystem free, retire oldest eligible Radarr-added movies, including unwatched, until at least 25% free.
- Retired records remain unmonitored; selection never re-adds or re-monitors them.
- Active/seeding/stopped torrents and multiply hardlinked files remain protected. If everything is protected, cleanup can stop without reaching 25%.
- Replenishment pauses below 15% free or when the estimate for existing pending movies plus the next movie exceeds available bytes. The estimate is 20 GiB per movie; it is not a filesystem reservation.
- Radarr maximum release size is 20480 MB. Indexer size metadata can be absent or inaccurate, so this is not an absolute storage guarantee.
- Transmission's download queue is enabled with size 1. Stalled-queue handling remains Transmission's existing policy.
- A tagged movie with no file after seven days may be unmonitored to unblock selection, but only with no queue entry and no Transmission torrents at all. This intentionally conservative rule does not automatically remove stalled torrents.
- No live movie deletion was forced for testing.

## Applications

- Radarr root `/downloads/library/movies` (ID 1), profile **4K - Original Audio** (ID 7): 2160p only, Original language, no automatic quality upgrades. The replenisher explicitly sets this profile for every automatic addition.
- Transmission uses `/downloads/torrents/movies`, with blank Radarr client category. All media shares host parent `/home/moura/transmission/downloads` for hardlinks.
- Completed import and hardlinks are enabled. Knaben seed ratio is 2.0; completed torrent removal waits for finished-seeding criteria.
- Prowlarr advertises `http://prowlarr:9696`, syncs fully to `http://radarr:7878`, and supplies Knaben RSS/automatic/interactive search.
- Jellyfin Movies points to `/data/library/movies` on the same host tree, mounted read-only. Portuguese-Portugal preference uses `pop`, not Brazilian `pob`. Default audio selection alone does not prove original-language playback.
- Bazarr at `http://192.168.1.202:6767` is movie-only, connected to Radarr, and configured for Portuguese (Portugal), Bazarr `pt` rather than `pb`.

## Deployment and credentials

`services/compose.media-addons.yaml` and `services/compose.media-cleanup.yaml` are separate Portainer stacks on RPi5 environment 4, sharing `lab_default`; they do not redeploy the existing lab stack. Paths, UID/GID 911 and disk source pins are specific to this installation.

Stage both `controller.py` and `replenish.py` under `/home/moura/bazarr/media-cleanup/` before deploying the cleanup manifest. That source directory, media and Radarr config are mounted read-only in the runner; `/state` is writable. The runner reads the Radarr API key from mounted `config.xml`. Never commit credentials.

Application API settings are persisted in app config directories, not created by Compose alone. Reproduce the profile/root/tag IDs and release/queue limits on a fresh installation. Replenishment defaults to dry-run; `--apply` is explicit. It uses mount verification, locking, API readbacks and a durable pre-add intent/cooldown.

## Verification and remaining limits

Run `python -m unittest discover -s scripts/media-cleanup -p 'test_*.py' -v`. The combined cleanup/replenishment suite passed 35 tests during initial integration. Validate Compose and read back the actual deployed source hashes, command and environment separately.

A real movie was automatically selected, searched, grabbed and observed transferring from peers. Completed import, Jellyfin playback and real retirement/replenishment of a full disk must be verified separately; do not infer them from running containers.

The user has no subtitle-provider account configured. Actual pt-PT subtitle retrieval remains blocked until an OpenSubtitles.com or LegendasDivx provider is configured and tested privately in Bazarr. Do not substitute Portuguese-Brazil.

1337x failed TLS verification; Internet Archive indexer testing timed out. Neither was kept and TLS verification was not disabled. Knaben is the active indexer. Source availability and discovery metadata can change; the runner fails closed on API or filesystem errors. Only acquire media you are authorized to download.
