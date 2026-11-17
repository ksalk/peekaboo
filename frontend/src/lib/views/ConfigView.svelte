<script lang="ts">
  import { onMount } from 'svelte';
  import { api, errorMessage } from '../api';
  import type { Camera } from '../types';

  let cameras = $state<Camera[]>([]);
  let loading = $state(true);
  let error = $state<string | null>(null);

  let showForm = $state(false);
  let editingId = $state<string | null>(null);
  let name = $state('');
  let url = $state('');
  let saving = $state(false);
  let formError = $state<string | null>(null);

  onMount(() => {
    void load();
  });

  async function load() {
    loading = true;
    error = null;

    try {
      cameras = await api.listCameras();
    } catch (cause) {
      error = errorMessage(cause);
    } finally {
      loading = false;
    }
  }

  function startCreate() {
    editingId = null;
    name = '';
    url = '';
    formError = null;
    showForm = true;
  }

  function startEdit(camera: Camera) {
    editingId = camera.id;
    name = camera.name;
    url = camera.url;
    formError = null;
    showForm = true;
  }

  function cancelForm() {
    showForm = false;
    formError = null;
  }

  async function save(event: SubmitEvent) {
    event.preventDefault();
    formError = null;
    saving = true;

    try {
      if (editingId) {
        const updated = await api.updateCamera(editingId, name.trim(), url.trim());
        cameras = cameras.map((camera) => (camera.id === updated.id ? updated : camera));
      } else {
        const created = await api.createCamera(name.trim(), url.trim());
        cameras = [...cameras, created].sort((left, right) => left.name.localeCompare(right.name));
      }

      showForm = false;
    } catch (cause) {
      formError = errorMessage(cause);
    } finally {
      saving = false;
    }
  }

  async function remove(camera: Camera) {
    if (!window.confirm(`Delete "${camera.name}"?`)) {
      return;
    }

    error = null;

    try {
      await api.deleteCamera(camera.id);
      cameras = cameras.filter((existing) => existing.id !== camera.id);
    } catch (cause) {
      error = errorMessage(cause);
    }
  }
</script>

<section class="page">
  <div class="page-header">
    <div>
      <h1>Cameras</h1>
      <p class="muted">Where your RTSP feeds live.</p>
    </div>
    <button class="button primary" onclick={startCreate}>Add camera</button>
  </div>

  <div class="callout">
    Camera usernames and passwords are injected from <code>CAMERA_USERNAME</code> and
    <code>CAMERA_PASSWORD</code> on the server. Never put credentials in the URL below.
  </div>

  {#if error}
    <div class="alert" role="alert">{error}</div>
  {/if}

  {#if showForm}
    <form class="card form-card" onsubmit={save}>
      <h2>{editingId ? 'Edit camera' : 'New camera'}</h2>

      <label>
        Name
        <input bind:value={name} maxlength="100" placeholder="Front door" required />
      </label>

      <label>
        RTSP URL
        <input
          bind:value={url}
          placeholder="rtsp://192.168.1.10:554/stream1"
          spellcheck="false"
          autocapitalize="none"
          required
        />
      </label>

      <p class="hint">
        Use the camera's H.264 substream when available: less bandwidth, less decoding load.
      </p>

      {#if formError}
        <div class="alert" role="alert">{formError}</div>
      {/if}

      <div class="actions">
        <button class="button ghost" type="button" onclick={cancelForm} disabled={saving}>
          Cancel
        </button>
        <button class="button primary" type="submit" disabled={saving}>
          {saving ? 'Saving…' : editingId ? 'Save changes' : 'Add camera'}
        </button>
      </div>
    </form>
  {/if}

  {#if loading}
    <p class="muted">Loading cameras…</p>
  {:else if cameras.length === 0}
    <div class="empty-state">
      <h2>No cameras yet</h2>
      <p>Add your first RTSP feed to get started.</p>
      <button class="button primary" onclick={startCreate}>Add camera</button>
    </div>
  {:else}
    <div class="camera-list">
      {#each cameras as camera (camera.id)}
        <article class="card camera-row">
          <div class="camera-info">
            <h2>{camera.name}</h2>
            <code class="url" title={camera.url}>{camera.url}</code>
          </div>
          <div class="actions">
            <button class="button small" onclick={() => startEdit(camera)}>Edit</button>
            <button class="button danger small" onclick={() => remove(camera)}>Delete</button>
          </div>
        </article>
      {/each}
    </div>
  {/if}
</section>
