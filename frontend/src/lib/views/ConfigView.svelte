<script lang="ts">
  import { onMount } from 'svelte';
  import { api, ApiError, errorMessage } from '../api';
  import type { Camera, Nvr } from '../types';

  type NvrForm = {
    id: string | null;
    name: string;
    baseUrl: string;
  };

  type CameraForm = {
    nvrId: string;
    id: string | null;
    name: string;
    mainStreamId: string;
    subStreamId: string;
  };

  let nvrs = $state<Nvr[]>([]);
  let cameras = $state<Camera[]>([]);
  let loading = $state(true);
  let error = $state<string | null>(null);

  let nvrForm = $state<NvrForm | null>(null);
  let nvrSaving = $state(false);
  let nvrFormError = $state<string | null>(null);

  let cameraForm = $state<CameraForm | null>(null);
  let cameraSaving = $state(false);
  let cameraFormError = $state<string | null>(null);

  onMount(() => {
    void load();
  });

  async function load() {
    loading = true;
    error = null;

    try {
      const [nextNvrs, nextCameras] = await Promise.all([
        api.listNvrs(),
        api.listCameras(),
      ]);
      nvrs = sortNvrs(nextNvrs);
      cameras = sortCameras(nextCameras);
    } catch (cause) {
      error = errorMessage(cause);
    } finally {
      loading = false;
    }
  }

  function camerasFor(nvrId: string): Camera[] {
    return cameras.filter((camera) => camera.nvrId === nvrId);
  }

  function startCreateNvr() {
    nvrForm = { id: null, name: '', baseUrl: '' };
    nvrFormError = null;
    cameraForm = null;
    cameraFormError = null;
  }

  function startEditNvr(nvr: Nvr) {
    nvrForm = { id: nvr.id, name: nvr.name, baseUrl: nvr.baseUrl };
    nvrFormError = null;
    cameraForm = null;
    cameraFormError = null;
  }

  function cancelNvrForm() {
    nvrForm = null;
    nvrFormError = null;
  }

  async function saveNvr(event: SubmitEvent) {
    event.preventDefault();
    const form = nvrForm;
    if (!form || nvrSaving) {
      return;
    }

    nvrFormError = null;
    nvrSaving = true;

    try {
      if (form.id) {
        const updated = await api.updateNvr(form.id, form.name.trim(), form.baseUrl.trim());
        nvrs = sortNvrs(nvrs.map((nvr) => (nvr.id === updated.id ? updated : nvr)));
        cameras = cameras.map((camera) =>
          camera.nvrId === updated.id ? { ...camera, nvrName: updated.name } : camera,
        );
      } else {
        const created = await api.createNvr(form.name.trim(), form.baseUrl.trim());
        nvrs = sortNvrs([...nvrs, created]);
      }

      if (nvrForm === form) {
        nvrForm = null;
      }
    } catch (cause) {
      nvrFormError = errorMessage(cause);
    } finally {
      nvrSaving = false;
    }
  }

  async function removeNvr(nvr: Nvr) {
    const cameraCount = camerasFor(nvr.id).length;
    const camerasText =
      cameraCount === 0
        ? 'No cameras are attached.'
        : `${cameraCount} camera${cameraCount === 1 ? '' : 's'} will be removed too.`;

    if (!window.confirm(`Delete "${nvr.name}"? ${camerasText}`)) {
      return;
    }

    error = null;

    try {
      await api.deleteNvr(nvr.id);
    } catch (cause) {
      if (!(cause instanceof ApiError && cause.status === 404)) {
        error = errorMessage(cause);
        return;
      }
    }

    nvrs = nvrs.filter((existing) => existing.id !== nvr.id);
    cameras = cameras.filter((camera) => camera.nvrId !== nvr.id);

    if (nvrForm?.id === nvr.id) {
      nvrForm = null;
    }

    if (cameraForm?.nvrId === nvr.id) {
      cameraForm = null;
    }
  }

  function startCreateCamera(nvrId: string) {
    cameraForm = {
      nvrId,
      id: null,
      name: '',
      mainStreamId: '',
      subStreamId: '',
    };
    cameraFormError = null;
    nvrForm = null;
    nvrFormError = null;
  }

  function startEditCamera(camera: Camera) {
    cameraForm = {
      nvrId: camera.nvrId,
      id: camera.id,
      name: camera.name,
      mainStreamId: camera.mainStreamId,
      subStreamId: camera.subStreamId,
    };
    cameraFormError = null;
    nvrForm = null;
    nvrFormError = null;
  }

  function cancelCameraForm() {
    cameraForm = null;
    cameraFormError = null;
  }

  async function saveCamera(event: SubmitEvent) {
    event.preventDefault();
    const form = cameraForm;
    if (!form || cameraSaving) {
      return;
    }

    cameraFormError = null;
    cameraSaving = true;

    try {
      const saved = form.id
        ? await api.updateCamera(
            form.nvrId,
            form.id,
            form.name.trim(),
            form.mainStreamId.trim(),
            form.subStreamId.trim(),
          )
        : await api.createCamera(
            form.nvrId,
            form.name.trim(),
            form.mainStreamId.trim(),
            form.subStreamId.trim(),
          );

      cameras = sortCameras([
        ...cameras.filter((camera) => camera.id !== saved.id),
        saved,
      ]);

      if (cameraForm === form) {
        cameraForm = null;
      }
    } catch (cause) {
      cameraFormError = errorMessage(cause);
    } finally {
      cameraSaving = false;
    }
  }

  async function removeCamera(camera: Camera) {
    if (!window.confirm(`Delete "${camera.name}"?`)) {
      return;
    }

    error = null;

    try {
      await api.deleteCamera(camera.nvrId, camera.id);
    } catch (cause) {
      if (!(cause instanceof ApiError && cause.status === 404)) {
        error = errorMessage(cause);
        return;
      }
    }

    cameras = cameras.filter((existing) => existing.id !== camera.id);

    if (cameraForm?.id === camera.id) {
      cameraForm = null;
    }
  }

  function sortNvrs(list: Nvr[]): Nvr[] {
    return [...list].sort((left, right) => left.name.localeCompare(right.name));
  }

  function sortCameras(list: Camera[]): Camera[] {
    return [...list].sort(
      (left, right) =>
        left.nvrName.localeCompare(right.nvrName) || left.name.localeCompare(right.name),
    );
  }
</script>

<section class="page">
  <div class="page-header">
    <div>
      <h1>NVRs</h1>
      <p class="muted">Hikvision NVRs and their ISAPI camera channels.</p>
    </div>
    {#if !nvrForm}
      <button
        class="button primary"
        onclick={startCreateNvr}
        disabled={nvrSaving || cameraSaving}
      >
        Add NVR
      </button>
    {/if}
  </div>

  <div class="callout">
    NVR usernames and passwords are injected from <code>CAMERA_USERNAME</code> and
    <code>CAMERA_PASSWORD</code> on the server. Never put credentials in the base URL.
  </div>

  {#if error}
    <div class="alert" role="alert">{error}</div>
  {/if}

  {#if nvrForm}
    <form class="card form-card" onsubmit={saveNvr}>
      <h2>{nvrForm.id ? 'Edit NVR' : 'New NVR'}</h2>

      <label>
        Name
        <input bind:value={nvrForm.name} maxlength="100" placeholder="House NVR" required />
      </label>

      <label>
        RTSP base URL
        <input
          bind:value={nvrForm.baseUrl}
          maxlength="2048"
          placeholder="rtsp://192.168.1.64:554"
          spellcheck="false"
          autocapitalize="none"
          required
        />
      </label>

      <p class="hint">
        Use the NVR's RTSP address, not its web UI URL. Peekaboo appends
        <code>/ISAPI/Streaming/channels/&lt;id&gt;</code>.
      </p>

      {#if nvrFormError}
        <div class="alert" role="alert">{nvrFormError}</div>
      {/if}

      <div class="actions">
        <button class="button ghost" type="button" onclick={cancelNvrForm} disabled={nvrSaving}>
          Cancel
        </button>
        <button class="button primary" type="submit" disabled={nvrSaving}>
          {nvrSaving ? 'Saving…' : nvrForm.id ? 'Save changes' : 'Add NVR'}
        </button>
      </div>
    </form>
  {/if}

  {#if loading}
    <p class="muted">Loading NVRs…</p>
  {:else if nvrs.length === 0}
    <div class="empty-state">
      <h2>No NVRs yet</h2>
      <p>Add an NVR, then add its cameras with their ISAPI main and sub stream IDs.</p>
    </div>
  {:else}
    <div class="nvr-list">
      {#each nvrs as nvr (nvr.id)}
        {@const nvrCameras = camerasFor(nvr.id)}
        <article class="card nvr-card">
          <header class="nvr-header">
            <div class="nvr-info">
              <h2>{nvr.name}</h2>
              <code class="url" title={nvr.baseUrl}>{nvr.baseUrl}</code>
            </div>

            <div class="actions">
              <button
                class="button small"
                aria-label={`Edit ${nvr.name}`}
                onclick={() => startEditNvr(nvr)}
                disabled={nvrSaving || cameraSaving}
              >
                Edit
              </button>
              <button
                class="button danger small"
                aria-label={`Delete ${nvr.name}`}
                onclick={() => removeNvr(nvr)}
                disabled={nvrSaving || cameraSaving}
              >
                Delete
              </button>
            </div>
          </header>

          <div class="nvr-cameras">
            {#if nvrCameras.length === 0}
              <p class="hint">No cameras yet.</p>
            {:else}
              <div class="camera-list">
                {#each nvrCameras as camera (camera.id)}
                  <div class="camera-row">
                    <div class="camera-info">
                      <h3>{camera.name}</h3>
                      <span class="stream-ids">
                        Main {camera.mainStreamId} · Sub {camera.subStreamId}
                      </span>
                    </div>

                    <div class="actions">
                      <button
                        class="button small"
                        aria-label={`Edit ${camera.name} on ${camera.nvrName}`}
                        onclick={() => startEditCamera(camera)}
                        disabled={cameraSaving || nvrSaving}
                      >
                        Edit
                      </button>
                      <button
                        class="button danger small"
                        aria-label={`Delete ${camera.name} on ${camera.nvrName}`}
                        onclick={() => removeCamera(camera)}
                        disabled={cameraSaving || nvrSaving}
                      >
                        Delete
                      </button>
                    </div>
                  </div>
                {/each}
              </div>
            {/if}

            {#if cameraForm && cameraForm.nvrId === nvr.id}
              <form class="camera-form" onsubmit={saveCamera}>
                <h3>{cameraForm.id ? 'Edit camera' : 'New camera'}</h3>

                <div class="form-grid">
                  <label>
                    Name
                    <input
                      bind:value={cameraForm.name}
                      maxlength="100"
                      placeholder="Front door"
                      required
                    />
                  </label>

                  <label>
                    Main stream ID
                    <input
                      bind:value={cameraForm.mainStreamId}
                      inputmode="numeric"
                      pattern="[1-9][0-9]*"
                      maxlength="6"
                      placeholder="101"
                      required
                    />
                  </label>

                  <label>
                    Sub stream ID
                    <input
                      bind:value={cameraForm.subStreamId}
                      inputmode="numeric"
                      pattern="[1-9][0-9]*"
                      maxlength="6"
                      placeholder="102"
                      required
                    />
                  </label>
                </div>

                <p class="hint">
                  ISAPI ID = channel × 100 + stream type: 101 = channel 1 main,
                  102 = channel 1 sub.
                </p>

                {#if cameraFormError}
                  <div class="alert" role="alert">{cameraFormError}</div>
                {/if}

                <div class="actions">
                  <button
                    class="button ghost"
                    type="button"
                    onclick={cancelCameraForm}
                    disabled={cameraSaving}
                  >
                    Cancel
                  </button>
                  <button class="button primary" type="submit" disabled={cameraSaving}>
                    {cameraSaving ? 'Saving…' : cameraForm.id ? 'Save changes' : 'Add camera'}
                  </button>
                </div>
              </form>
            {:else}
              <div class="actions">
                <button
                  class="button small"
                  aria-label={`Add camera to ${nvr.name}`}
                  onclick={() => startCreateCamera(nvr.id)}
                  disabled={cameraSaving || nvrSaving}
                >
                  Add camera
                </button>
              </div>
            {/if}
          </div>
        </article>
      {/each}
    </div>
  {/if}
</section>
