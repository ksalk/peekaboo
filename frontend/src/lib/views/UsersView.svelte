<script lang="ts">
  import { onMount } from 'svelte';
  import { api, errorMessage } from '../api';
  import { auth } from '../auth.svelte';
  import { formatDate } from '../date-time';
  import type { User } from '../types';

  let users = $state<User[]>([]);
  let loading = $state(true);
  let error = $state<string | null>(null);

  let showAdd = $state(false);
  let username = $state('');
  let password = $state('');
  let isAdmin = $state(false);
  let saving = $state(false);
  let formError = $state<string | null>(null);

  let resetFor = $state<string | null>(null);
  let resetPassword = $state('');
  let resetError = $state<string | null>(null);
  let resetting = $state(false);

  onMount(() => {
    void load();
  });

  async function load() {
    loading = true;
    error = null;

    try {
      users = await api.listUsers();
    } catch (cause) {
      error = errorMessage(cause);
    } finally {
      loading = false;
    }
  }

  function startAdd() {
    username = '';
    password = '';
    isAdmin = false;
    formError = null;
    showAdd = true;
  }

  async function createUser(event: SubmitEvent) {
    event.preventDefault();
    formError = null;
    saving = true;

    try {
      const created = await api.createUser(username.trim(), password, isAdmin);
      users = [...users, created].sort((left, right) =>
        left.username.localeCompare(right.username),
      );
      showAdd = false;
    } catch (cause) {
      formError = errorMessage(cause);
    } finally {
      saving = false;
    }
  }

  async function removeUser(user: User) {
    if (!window.confirm(`Delete user "${user.username}"?`)) {
      return;
    }

    error = null;

    try {
      await api.deleteUser(user.id);
      users = users.filter((existing) => existing.id !== user.id);
    } catch (cause) {
      error = errorMessage(cause);
    }
  }

  function startReset(user: User) {
    resetFor = user.id;
    resetPassword = '';
    resetError = null;
  }

  async function submitReset(event: SubmitEvent, user: User) {
    event.preventDefault();
    resetError = null;
    resetting = true;

    try {
      await api.resetPassword(user.id, resetPassword);
      resetFor = null;
    } catch (cause) {
      resetError = errorMessage(cause);
    } finally {
      resetting = false;
    }
  }
</script>

<section class="page">
  <div class="page-header">
    <div>
      <h1>Users</h1>
      <p class="muted">Accounts that can open Peekaboo.</p>
    </div>
    <button class="button primary" onclick={startAdd}>Add user</button>
  </div>

  {#if error}
    <div class="alert" role="alert">{error}</div>
  {/if}

  {#if showAdd}
    <form class="card form-card" onsubmit={createUser}>
      <h2>New user</h2>

      <label>
        Username
        <input
          bind:value={username}
          autocomplete="off"
          autocapitalize="none"
          spellcheck="false"
          minlength="3"
          maxlength="64"
          required
        />
      </label>

      <label>
        Password
        <input
          type="password"
          bind:value={password}
          autocomplete="new-password"
          minlength="8"
          maxlength="256"
          required
        />
      </label>

      <label class="checkbox">
        <input type="checkbox" bind:checked={isAdmin} />
        Administrator
      </label>

      {#if formError}
        <div class="alert" role="alert">{formError}</div>
      {/if}

      <div class="actions">
        <button class="button ghost" type="button" onclick={() => (showAdd = false)} disabled={saving}>
          Cancel
        </button>
        <button class="button primary" type="submit" disabled={saving}>
          {saving ? 'Creating…' : 'Create user'}
        </button>
      </div>
    </form>
  {/if}

  {#if loading}
    <p class="muted">Loading users…</p>
  {:else}
    <div class="user-list">
      {#each users as user (user.id)}
        <article class="card user-row">
          <div class="user-info">
            <h2>
              {user.username}
              {#if user.id === auth.user?.id}<span class="badge">you</span>{/if}
              {#if user.isAdmin}<span class="badge accent">admin</span>{/if}
            </h2>
            <span class="muted small-text">
              Joined {formatDate(user.createdAt)}
            </span>

            {#if resetFor === user.id}
              <form class="inline-form" onsubmit={(event) => submitReset(event, user)}>
                <input
                  type="password"
                  bind:value={resetPassword}
                  placeholder="New password (8+ characters)"
                  minlength="8"
                  maxlength="256"
                  autocomplete="new-password"
                  required
                />
                <button class="button small" type="submit" disabled={resetting}>
                  {resetting ? 'Saving…' : 'Set password'}
                </button>
                <button class="button ghost small" type="button" onclick={() => (resetFor = null)}>
                  Cancel
                </button>
              </form>
              {#if resetError}
                <div class="alert" role="alert">{resetError}</div>
              {/if}
            {/if}
          </div>

          <div class="actions">
            <button class="button small" onclick={() => startReset(user)}>Reset password</button>
            {#if user.id !== auth.user?.id}
              <button class="button danger small" onclick={() => removeUser(user)}>Delete</button>
            {/if}
          </div>
        </article>
      {/each}
    </div>
  {/if}
</section>
