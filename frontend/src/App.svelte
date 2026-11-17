<script lang="ts">
  import { onMount } from 'svelte';
  import { auth } from './lib/auth.svelte';
  import LoginView from './lib/views/LoginView.svelte';
  import ConfigView from './lib/views/ConfigView.svelte';
  import UsersView from './lib/views/UsersView.svelte';
  import AccountView from './lib/views/AccountView.svelte';

  type Route = 'config' | 'users' | 'account';

  function readRoute(): Route {
    const value = window.location.hash.replace(/^#\/?/, '');
    return value === 'users' || value === 'account' ? value : 'config';
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
      window.location.hash = '#/config';
    }
  });

  async function logout() {
    loggingOut = true;
    try {
      await auth.logout();
      window.location.hash = '#/config';
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
      <a class="brand" href="#/config">
        <span class="brand-dot" aria-hidden="true"></span>
        Peekaboo
      </a>

      <nav class="main-nav" aria-label="Main navigation">
        <a href="#/config" class:active={route === 'config'}>Cameras</a>
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
      {:else if route === 'account'}
        <AccountView />
      {:else}
        <ConfigView />
      {/if}
    </main>
  </div>
{/if}
