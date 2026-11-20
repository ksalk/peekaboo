<script lang="ts">
  import { onMount } from 'svelte';
  import { auth } from './lib/auth.svelte';
  import LoginView from './lib/views/LoginView.svelte';
  import FeedsView from './lib/views/FeedsView.svelte';
  import PlaybackView from './lib/views/PlaybackView.svelte';
  import ConfigView from './lib/views/ConfigView.svelte';
  import UsersView from './lib/views/UsersView.svelte';
  import AccountView from './lib/views/AccountView.svelte';

  type Route = 'feeds' | 'playback' | 'config' | 'users' | 'account';

  function readRoute(): Route {
    const value = window.location.hash.replace(/^#\/?/, '');
    return value === 'playback' || value === 'config' || value === 'users' || value === 'account'
      ? value
      : 'feeds';
  }

  let route = $state<Route>(readRoute());
  let loggingOut = $state(false);

  onMount(() => {
    void auth.load();

    const updateRoute = () => {
      route = readRoute();
    };

    window.addEventListener('hashchange', updateRoute);
    return () => window.removeEventListener('hashchange', updateRoute);
  });

  $effect(() => {
    if (auth.user && route === 'users' && !auth.user.isAdmin) {
      window.location.hash = '#/feeds';
    }
  });

  async function logout() {
    loggingOut = true;
    try {
      await auth.logout();
      window.location.hash = '#/feeds';
    } catch (cause) {
      console.error(cause);
    } finally {
      loggingOut = false;
    }
  }
</script>

{#if auth.loading}
  <div class="boot-screen">
    <span class="spinner" aria-hidden="true"></span>
    <p>Loading Peekaboo…</p>
  </div>
{:else if !auth.user}
  <LoginView />
{:else}
  <div class="shell">
    <header class="topbar">
      <a class="brand" href="#/feeds">
        <span class="brand-dot" aria-hidden="true"></span>
        Peekaboo
      </a>

      <nav class="main-nav" aria-label="Main navigation">
        <a href="#/feeds" class:active={route === 'feeds'}>Feeds</a>
        <a href="#/playback" class:active={route === 'playback'}>Playback</a>
        <a href="#/config" class:active={route === 'config'}>NVRs</a>
        {#if auth.user.isAdmin}
          <a href="#/users" class:active={route === 'users'}>Users</a>
        {/if}
        <a href="#/account" class:active={route === 'account'}>Account</a>
      </nav>

      <div class="topbar-user">
        <span class="username">{auth.user.username}</span>
        <button class="button ghost small" onclick={logout} disabled={loggingOut}>
          {loggingOut ? 'Signing out…' : 'Sign out'}
        </button>
      </div>
    </header>

    <main class="content">
      {#if route === 'users' && auth.user.isAdmin}
        <UsersView />
      {:else if route === 'playback'}
        <PlaybackView />
      {:else if route === 'config'}
        <ConfigView />
      {:else if route === 'account'}
        <AccountView />
      {:else}
        <FeedsView />
      {/if}
    </main>
  </div>
{/if}
