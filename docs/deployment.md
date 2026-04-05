# Deployment

## Gateway

The gateway is designed to run as a Docker container in front of Aurral.

Minimum configuration:

- `JELLYFIN_URL`
- `JELLYFIN_PUBLIC_URL`
- `AURRAL_URL`
- `COOKIE_SECRET`
- `HANDOFF_SECRET`

## Aurral

Recommended settings:

- `AUTH_PROXY_ENABLED=true`
- `AUTH_PROXY_HEADER=X-Forwarded-User`
- `AUTH_PROXY_ROLE_HEADER=X-Forwarded-Role`
- `AUTH_PROXY_DEFAULT_ROLE=user`
- `AUTH_PROXY_TRUSTED_IPS=<gateway-ip>`
- `TRUST_PROXY=true`

Important: do not leave `AUTH_PROXY_TRUSTED_IPS` empty in production.

## Jellyfin Plugin

1. Build the plugin with `dotnet build` or `dotnet publish`
2. Copy the output DLLs into a subfolder under Jellyfin's `plugins/` directory
3. Restart Jellyfin
4. Open the plugin configuration page from the Jellyfin dashboard and fill in:
   - gateway base URL
   - gateway handoff path
   - shared handoff secret
   - token lifetime

## Public Routing

Example public host split:

- `media.example.com` -> Jellyfin
- `accounts.example.com` -> jfa-go
- `music.example.com` -> Jellygate

The gateway does not require a particular tunnel or reverse proxy implementation. `cloudflared`, Traefik, Caddy, Nginx, and plain LAN routing all work as long as the configured URLs match reality.
