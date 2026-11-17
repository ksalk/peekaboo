<script lang="ts">
  import { api, errorMessage } from '../api';

  let currentPassword = $state('');
  let newPassword = $state('');
  let confirmPassword = $state('');
  let error = $state<string | null>(null);
  let success = $state<string | null>(null);
  let saving = $state(false);

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    error = null;
    success = null;

    if (newPassword !== confirmPassword) {
      error = 'The new passwords do not match.';
      return;
    }

    saving = true;

    try {
      await api.changePassword(currentPassword, newPassword);
      currentPassword = '';
      newPassword = '';
      confirmPassword = '';
      success = 'Password updated.';
    } catch (cause) {
      error = errorMessage(cause);
    } finally {
      saving = false;
    }
  }
</script>

<section class="page narrow">
  <div class="page-header">
    <div>
      <h1>Account</h1>
      <p class="muted">Change the password for your account.</p>
    </div>
  </div>

  <form class="card form-card" onsubmit={submit}>
    <label>
      Current password
      <input
        type="password"
        bind:value={currentPassword}
        autocomplete="current-password"
        maxlength="256"
        required
      />
    </label>

    <label>
      New password
      <input
        type="password"
        bind:value={newPassword}
        autocomplete="new-password"
        minlength="8"
        maxlength="256"
        required
      />
    </label>

    <label>
      Confirm new password
      <input
        type="password"
        bind:value={confirmPassword}
        autocomplete="new-password"
        minlength="8"
        maxlength="256"
        required
      />
    </label>

    {#if error}
      <div class="alert" role="alert">{error}</div>
    {/if}

    {#if success}
      <div class="alert success" role="status">{success}</div>
    {/if}

    <div class="actions">
      <button class="button primary" type="submit" disabled={saving}>
        {saving ? 'Updating…' : 'Update password'}
      </button>
    </div>
  </form>
</section>
