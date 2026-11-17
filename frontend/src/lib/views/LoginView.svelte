<script lang="ts">
  import { auth } from '../auth.svelte';
  import { errorMessage } from '../api';

  let username = $state('');
  let password = $state('');
  let error = $state<string | null>(null);
  let busy = $state(false);

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    error = null;
    busy = true;

    try {
      await auth.login(username.trim(), password);
    } catch (cause) {
      error = errorMessage(cause);
    } finally {
      busy = false;
    }
  }
</script>

<div class="login-screen">
  <form class="login-card" onsubmit={submit}>
    <div class="login-brand">
      <span class="brand-dot large" aria-hidden="true"></span>
      <h1>Peekaboo</h1>
    </div>
    <p class="muted login-tagline">Your cameras, only when you want them.</p>

    {#if error}
      <div class="alert" role="alert">{error}</div>
    {/if}

    <label>
      Username
      <input
        bind:value={username}
        autocomplete="username"
        autocapitalize="none"
        spellcheck="false"
        required
      />
    </label>

    <label>
      Password
      <input
        type="password"
        bind:value={password}
        autocomplete="current-password"
        required
      />
    </label>

    <button
      class="button primary wide"
      type="submit"
      disabled={busy || !username.trim() || !password}
    >
      {busy ? 'Signing in…' : 'Sign in'}
    </button>
  </form>
</div>
