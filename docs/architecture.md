# Architecture

## Why Jellygate Exists

`Aurral` already supports reverse-proxy authentication, but it does not authenticate directly against Jellyfin or `jfa-go`.

`Jellygate` keeps the source of truth where it already lives:

- `Jellyfin`: credentials and admin policy
- `jfa-go`: invite flow and account lifecycle
- `Aurral`: music discovery and requests

Jellygate only bridges identity into Aurral.

## Repo Layout

```text
Jellygate/
  apps/
    gateway/          # Fastify auth gateway and Aurral reverse proxy
    web/              # Svelte login UI
  plugins/
    Jellygate.JellyfinBridge/
                      # Jellyfin plugin for signed handoff redirects
  docs/
```

## Runtime Components

### Gateway

Responsibilities:

- validate direct username/password login against Jellyfin
- manage its own signed session cookie
- validate short-lived handoff tokens from the Jellyfin plugin
- proxy requests into Aurral while injecting trusted identity headers

It never persists Jellyfin credentials and does not need a database for the MVP.

### Web UI

Responsibilities:

- render the Jellygate login page
- call gateway auth endpoints
- start the Jellyfin handoff flow when available
- redirect back into Aurral after login

### Jellyfin Bridge Plugin

Responsibilities:

- expose an authenticated route on the Jellyfin origin
- inspect the current Jellyfin web user
- sign a short-lived handoff token with a shared secret
- redirect the browser back to Jellygate
- expose a basic admin configuration surface inside Jellyfin

## Security Model

### Gateway session cookie

The gateway issues a signed session cookie containing:

- user id
- username
- role
- issued-at timestamp
- expiry timestamp

The cookie is signed with `COOKIE_SECRET` and scoped to the Jellygate host.

### Handoff token

The plugin issues a separate short-lived token signed with `HANDOFF_SECRET`.

Payload fields:

- `iss`: `jellygate-bridge`
- `sub`: Jellyfin user id
- `name`: Jellyfin username
- `role`: `admin` or `user`
- `iat`
- `exp`
- `jti`
- optional `returnTo`

The gateway validates the signature and expiry before creating its own session.

### Aurral trust boundary

Jellygate strips any client-supplied forwarded identity headers and sets:

- `X-Forwarded-User`
- `X-Forwarded-Role`

For production, Aurral must trust only Jellygate's proxy IP via `AUTH_PROXY_TRUSTED_IPS`.

## Request Flow

### Direct login

```text
Browser
  -> Jellygate UI
  -> Jellygate POST /auth/login
  -> Jellyfin POST /Users/AuthenticateByName
  -> Jellygate session cookie
  -> Jellygate reverse proxy
  -> Aurral
```

### Handoff from an existing Jellyfin session

```text
Browser
  -> Jellyfin plugin route /JellygateBridge/start
  -> Jellyfin resolves current user
  -> Plugin signs short-lived token
  -> Redirect to Jellygate /auth/handoff?token=...
  -> Jellygate validates token and issues session cookie
  -> Jellygate reverse proxy
  -> Aurral
```

## Configuration Boundaries

### Gateway configuration

The gateway is environment-driven so it stays portable across Docker, Unraid, compose, and Kubernetes-style deployments.

### Plugin configuration

The plugin keeps Jellyfin-side bridge configuration inside the Jellyfin plugin configuration store so admins do not need to edit raw files.

## Future Extensions

- Redis-backed session store
- refreshable handoff flows
- plugin menu integration inside Jellyfin web
- optional SCIM-style user sync into Aurral if Aurral later supports persistent proxy users
- additional reverse-proxy upstreams beyond Aurral
