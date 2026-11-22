<script lang="ts">
  let {
    label,
    detail = null,
  }: {
    label: string;
    detail?: string | null;
  } = $props();
</script>

<div class="search-status" role="status" aria-live="polite">
  <span class="spinner" aria-hidden="true"></span>
  <div class="search-status-text">
    <strong>{label}</strong>
    {#if detail}
      <span class="muted">{detail}</span>
    {/if}
  </div>
</div>
