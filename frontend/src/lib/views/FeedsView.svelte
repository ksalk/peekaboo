<script lang="ts">
  import { onMount, tick } from 'svelte';
  import { api, errorMessage } from '../api';
  import type { Camera } from '../types';
  import FeedCell from '../components/FeedCell.svelte';

  const LAYOUT_KEY = 'peekaboo.layout';
  const ASSIGNMENTS_PREFIX = 'peekaboo.assignments.';

  const layouts = [
    { id: '1x1', label: '1 × 1', cells: 1 },
    { id: '1x2', label: '1 × 2', cells: 2 },
    { id: '2x1', label: '2 × 1', cells: 2 },
    { id: '2x2', label: '2 × 2', cells: 4 },
    { id: '3x3', label: '3 × 3', cells: 9 },
  ] as const;

  type LayoutId = (typeof layouts)[number]['id'];

  const initialLayout = readLayout();

  let layout = $state<LayoutId>(initialLayout);
  let assignments = $state<(string | null)[]>(readAssignments(initialLayout));
  let cameras = $state<Camera[]>([]);
  let loading = $state(true);
  let error = $state<string | null>(null);
  let pickerCell = $state<number | null>(null);
  let stopAllSignal = $state(0);
  let dialogElement = $state<HTMLDivElement>();
  let returnFocus: HTMLElement | null = null;

  const cellCount = $derived(layouts.find((item) => item.id === layout)?.cells ?? 1);
  const cellIndices = $derived(Array.from({ length: cellCount }, (_, index) => index));

  onMount(() => {
    void loadCameras();
  });

  async function loadCameras() {
    loading = true;
    error = null;

    try {
      cameras = await api.listCameras();
      assignments = sanitizeAssignments(assignments);
    } catch (cause) {
      error = errorMessage(cause);
    } finally {
      loading = false;
    }
  }

  function setLayout(next: LayoutId) {
    if (next === layout) {
      return;
    }

    stopAllSignal += 1;
    layout = next;

    try {
      localStorage.setItem(LAYOUT_KEY, next);
    } catch {
      // Local storage can be unavailable in private or restricted contexts.
    }

    assignments = sanitizeAssignments(readAssignments(next));
    persistAssignments();
  }

  async function openPicker(index: number) {
    returnFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    pickerCell = index;
    await tick();
    dialogElement?.focus();
  }

  async function closePicker() {
    pickerCell = null;
    await tick();

    if (returnFocus?.isConnected) {
      returnFocus.focus();
    }

    returnFocus = null;
  }

  function assignCamera(cameraId: string) {
    if (pickerCell === null) {
      return;
    }

    const next = [...assignments];
    next[pickerCell] = cameraId;
    assignments = next;
    persistAssignments();
    void closePicker();
  }

  function clearCell() {
    if (pickerCell === null) {
      return;
    }

    const next = [...assignments];
    next[pickerCell] = null;
    assignments = next;
    persistAssignments();
    void closePicker();
  }

  function cameraFor(index: number): Camera | null {
    const id = assignments[index];
    return id ? (cameras.find((camera) => camera.id === id) ?? null) : null;
  }

  function isSelected(cameraId: string): boolean {
    return pickerCell !== null && assignments[pickerCell] === cameraId;
  }

  function sanitizeAssignments(list: (string | null)[]): (string | null)[] {
    const validIds = new Set(cameras.map((camera) => camera.id));
    return list.map((id) => (id && validIds.has(id) ? id : null));
  }

  function persistAssignments() {
    try {
      localStorage.setItem(ASSIGNMENTS_PREFIX + layout, JSON.stringify(assignments));
    } catch {
      // Local storage can be unavailable in private or restricted contexts.
    }
  }

  function readLayout(): LayoutId {
    try {
      const stored = localStorage.getItem(LAYOUT_KEY);
      return layouts.find((item) => item.id === stored)?.id ?? '1x1';
    } catch {
      return '1x1';
    }
  }

  function readAssignments(forLayout: LayoutId): (string | null)[] {
    const count = layouts.find((item) => item.id === forLayout)?.cells ?? 1;
    const result: (string | null)[] = Array.from({ length: count }, () => null);

    try {
      const stored = localStorage.getItem(ASSIGNMENTS_PREFIX + forLayout);
      if (!stored) {
        return result;
      }

      const parsed: unknown = JSON.parse(stored);
      if (!Array.isArray(parsed)) {
        return result;
      }

      parsed.slice(0, count).forEach((value, index) => {
        if (typeof value === 'string') {
          result[index] = value;
        }
      });
    } catch {
      // Corrupt local storage should never prevent the grid from loading.
    }

    return result;
  }

  function handleKeydown(event: KeyboardEvent) {
    if (event.key === 'Escape' && pickerCell !== null) {
      void closePicker();
    }
  }
</script>

<svelte:window onkeydown={handleKeydown} />

<section class="page">
  <div class="page-header">
    <div>
      <h1>Live</h1>
      <p class="muted">Streams only run while you are watching them.</p>
    </div>

    <div class="feed-controls">
      <div class="layout-picker" role="group" aria-label="Grid layout">
        {#each layouts as item (item.id)}
          <button
            type="button"
            class:active={layout === item.id}
            aria-pressed={layout === item.id}
            onclick={() => setLayout(item.id)}
          >
            {item.label}
          </button>
        {/each}
      </div>

      <button class="button small" onclick={() => (stopAllSignal += 1)}>Stop all</button>
    </div>
  </div>

  {#if error}
    <div class="alert" role="alert">{error}</div>
  {/if}

  {#if loading}
    <p class="muted">Loading cameras…</p>
  {:else if cameras.length === 0}
    <div class="empty-state">
      <h2>No cameras configured</h2>
      <p>Add an NVR and its cameras first, then come back to build your grid.</p>
      <a class="button primary" href="#/config">Add NVRs</a>
    </div>
  {:else}
    <p class="hint feed-hint-line">
      Click a cell to choose its camera. Assignments are remembered on this device; feeds
      never start automatically.
    </p>

    <div class="feed-grid layout-{layout}">
      {#each cellIndices as index (index)}
        {@const camera = cameraFor(index)}
        <FeedCell
          {camera}
          stopSignal={stopAllSignal}
          onChoose={() => void openPicker(index)}
        />
      {/each}
    </div>
  {/if}
</section>

{#if pickerCell !== null}
  <div
    class="modal-backdrop"
    role="presentation"
    onclick={(event) => {
      if (event.target === event.currentTarget) {
        void closePicker();
      }
    }}
  >
    <div
      class="modal"
      bind:this={dialogElement}
      role="dialog"
      aria-modal="true"
      aria-labelledby="camera-picker-title"
      tabindex="-1"
    >
      <h2 id="camera-picker-title">Choose camera</h2>

      {#if cameras.length === 0}
        <p class="muted">
          No cameras configured yet. <a href="#/config">Add one first</a>.
        </p>
      {:else}
        <div class="picker-list">
          {#each cameras as camera (camera.id)}
            <button
              type="button"
              class="picker-option"
              class:selected={isSelected(camera.id)}
              onclick={() => assignCamera(camera.id)}
            >
              <strong>{camera.name}</strong>
              <span class="url">
                {camera.nvrName} · main {camera.mainStreamId} · sub {camera.subStreamId}
              </span>
            </button>
          {/each}
        </div>
      {/if}

      <div class="actions picker-actions">
        {#if assignments[pickerCell]}
          <button class="button danger small" onclick={clearCell}>Clear cell</button>
        {/if}
        <button class="button ghost small" onclick={() => void closePicker()}>Cancel</button>
      </div>
    </div>
  </div>
{/if}
