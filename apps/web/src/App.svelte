<script lang="ts">
  import { onMount } from 'svelte';

  import './app.css';
  import { fetchPublicConfig, fetchSession, login, type PublicConfig } from './lib/api';

  let config: PublicConfig | null = null;
  let username = '';
  let password = '';
  let loading = true;
  let busy = false;
  let error = '';

  const search = new URLSearchParams(window.location.search);
  const returnTo = sanitizeReturnTo(search.get('returnTo'));

  onMount(async () => {
    const [publicConfig, session] = await Promise.all([fetchPublicConfig(), fetchSession()]);
    config = publicConfig;

    if (session.authenticated) {
      window.location.href = returnTo;
      return;
    }

    loading = false;
  });

  async function handleLogin(event: SubmitEvent) {
    event.preventDefault();
    busy = true;
    error = '';

    try {
      const response = await login({ username, password, returnTo });
      window.location.href = response.returnTo;
    } catch (cause) {
      error = cause instanceof Error ? cause.message : 'Login failed';
      busy = false;
    }
  }

  function continueWithJellyfin() {
    if (!config?.continueWithJellyfinUrl) {
      return;
    }

    const target = new URL(config.continueWithJellyfinUrl);
    target.searchParams.set('returnTo', returnTo);
    window.location.href = target.toString();
  }

  function sanitizeReturnTo(input: string | null): string {
    if (!input || !input.startsWith('/') || input.startsWith('//')) {
      return '/';
    }

    return input;
  }
</script>

{#if loading}
  <div class="shell">
    <div class="card">
      <p>Loading Jellygate...</p>
    </div>
  </div>
{:else}
  <div class="shell">
    <div class:busy={busy} class="card">
      <div class="eyebrow">Jellygate</div>
      <h1>{config?.appName ?? 'Jellygate'}</h1>
      <p>Sign in with your Jellyfin account to access Aurral through a trusted proxy session.</p>

      {#if config?.allowPasswordLogin}
        <form on:submit={handleLogin}>
          <label>
            Username
            <input bind:value={username} autocomplete="username" required />
          </label>

          <label>
            Password
            <input bind:value={password} autocomplete="current-password" type="password" required />
          </label>

          <div class="actions">
            <button class="primary" type="submit">Continue</button>

            {#if config?.allowJellyfinHandoff && config?.continueWithJellyfinUrl}
              <button class="secondary" on:click|preventDefault={continueWithJellyfin} type="button">
                Continue with Jellyfin
              </button>
            {/if}
          </div>
        </form>
      {:else if config?.allowJellyfinHandoff && config?.continueWithJellyfinUrl}
        <div class="actions">
          <button class="primary" on:click={continueWithJellyfin} type="button">Continue with Jellyfin</button>
        </div>
      {/if}

      {#if error}
        <p class="error">{error}</p>
      {/if}

      <div class="meta">
        {#if config?.signupUrl}
          <a class="link" href={config.signupUrl}>Create account</a>
        {/if}

        {#if config?.passwordResetUrl}
          <a class="link" href={config.passwordResetUrl}>Reset password</a>
        {/if}
      </div>
    </div>
  </div>
{/if}
