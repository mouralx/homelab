# RPi5 movie automation

## Live configuration

Movie pipeline: Prowlarr -> Radarr -> Transmission -> organized Movies -> Jellyfin.

- Radarr root: `/downloads/library/movies` (root ID 1).
- New Radarr profile: **4K - Original Audio** (ID 7). Only 2160p qualities, Original language, no automatic quality upgrades. **Select this profile when adding movies**; creating it does not change other profiles or browser defaults.
- Transmission client: `transmission:9091`, directory `/downloads/torrents/movies`. Radarr's Transmission adapter rejects simultaneous Category and Directory: Directory is set, Category blank. No other downloads belong in this dedicated directory.
- Completed-download handling and hardlinks are enabled. An isolated hardlink test passed as Radarr's `abc` user across torrent/library folders.
- Prowlarr Radarr application uses `http://radarr:7878`, advertised Prowlarr URL `http://prowlarr:9696`, full sync. Knaben passed its connection test and synced RSS, automatic and interactive search into Radarr.
- Knaben uses seed ratio 2.0. Radarr removes completed torrent data only after its finished-seeding criteria are met, retaining imported library hardlinks. Active/seeding downloads remain protected. Any torrent still retained by Transmission or file with multiple hardlinks must be excluded by the cleanup controller.
- Jellyfin Movies location: `/data/library/movies`; same host media tree mounted read-only. Account subtitle preference `pop` is Jellyfin's Portuguese (Portugal) code; `pob` is Brazil. Default audio track preference is not a guarantee of original-language playback.
- Bazarr: `http://192.168.1.202:6767`, private generated forms credentials, Radarr enabled, Sonarr disabled, shared `/downloads` path (no remapping required). Default movie language profile **Portuguese (Portugal)** uses Bazarr `pt`, not `pb` (Brazil).

## Deployment

`services/compose.media-addons.yaml` defines the independent **media-addons** stack on RPi5. It reuses external network `lab_default` and does not redeploy existing applications. The live stack is managed through Portainer environment 4. Do not run both a second differently named stack and this stack with the same container names.

The paths, LAN IP and LinuxServer UID/GID 911 are specific to this rootless-Docker installation; inspect before applying elsewhere. Persisted application configuration lives in each app's `/config` bind, not in Git. No API keys or passwords belong in Compose or this repository. A deployment of the manifest alone does not reproduce application API settings on fresh empty config directories; apply the settings listed above through authenticated app interfaces.

## Subtitle blocker

The user currently has no subtitle-provider account. Bazarr is configured but **automatic subtitle downloads are not verified or operational until providers are configured and tested**. Create an OpenSubtitles.com or LegendasDivx account, then enter it privately in Bazarr Settings -> Providers. Do not enable Portuguese (Brazil) as a fallback. Match quality and availability depend on provider/release coverage.

## Verification and limitations

Connection tests for Prowlarr -> Radarr and Radarr -> Transmission passed; Knaben test and Radarr indexer read-back passed. Bazarr reports Radarr version and an empty health-error list; unauthenticated settings API returns 401. Jellyfin library and account settings were read back.

1337x failed TLS certificate verification from the Prowlarr container; SSL verification was not disabled. Internet Archive indexer testing timed out (its website itself returned HTTP 200). Neither failing definition was saved. Knaben is the tested alternative.

No movie has been added or acquired as a test. A complete real download/import/playback/subtitle round trip is therefore **untested**. Add only media you are authorized to download. An empty Jellyfin library is expected until media arrives.

## Approved cleanup policy

The user explicitly confirmed: trigger below 15% actual media filesystem free, retire oldest Radarr-added eligible movies (including unwatched) until at least 25% free, protecting active/seeding downloads. Implementation/test/deployment status must be checked separately; this approval does not itself enable deletion. No actual user media should be deleted for testing. Conservative skipped files may prevent reaching the target; report that rather than weakening protection.
