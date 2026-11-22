<script lang="ts">
  import type { Snippet } from 'svelte';

  let {
    x,
    y,
    above = true,
    interactive = false,
    children,
    onpointerenter = null,
    onpointerleave = null,
    onfocusout = null,
  }: {
    x: number;
    y: number;
    above?: boolean;
    interactive?: boolean;
    children: Snippet;
    onpointerenter?: (() => void) | null;
    onpointerleave?: (() => void) | null;
    onfocusout?: ((event: FocusEvent) => void) | null;
  } = $props();
</script>

<div
  class="floating-tooltip"
  class:above
  class:below={!above}
  class:interactive
  role="tooltip"
  style={`left:${x}px;top:${y}px`}
  onpointerenter={onpointerenter}
  onpointerleave={onpointerleave}
  onfocusout={onfocusout}
>
  {@render children()}
</div>
