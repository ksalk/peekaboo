<script module lang="ts">
  let nextId = 0;

  function makeId(): string {
    nextId += 1;
    return `date-time-${nextId}`;
  }
</script>

<script lang="ts">
  import { tick } from 'svelte';
  import { SvelteDate } from 'svelte/reactivity';
  import {
    buildLocalDateTime,
    formatDateTime,
    parseDisplayDateTime,
    parseLocalDateTimeInput,
    toDisplayDateTime,
    toLocalDateTimeInput,
  } from '../date-time';

  let {
    value = $bindable(null),
    label,
    id,
    required = false,
    disabled = false,
    hint = null,
    onchange = null,
  }: {
    value?: string | null;
    label: string;
    id?: string;
    required?: boolean;
    disabled?: boolean;
    hint?: string | null;
    /** Called with the parsed local value, or null while the text is not valid. */
    onchange?: ((value: string | null) => void) | null;
  } = $props();

  // svelte-ignore state_referenced_locally
  const inputId = id ?? makeId();
  const errorId = `${inputId}-error`;
  const hintId = `${inputId}-hint`;
  const monthFormat = new Intl.DateTimeFormat('en-GB', {
    month: 'long',
    year: 'numeric',
  });
  const weekdays = ['Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su'];

  let text = $state(value ? toDisplayDateTime(value) : '');
  let error = $state<string | null>(null);
  let dateOpen = $state(false);
  let timeOpen = $state(false);
  let root = $state<HTMLDivElement>();
  let inputElement = $state<HTMLInputElement>();
  let datePopover = $state<HTMLDivElement>();
  let timePopover = $state<HTMLDivElement>();
  let lastValue = value;
  let calendarMonth = $state.raw(new SvelteDate(startOfMonth(valueDate() ?? new Date())));

  type CalendarDay = {
    date: Date;
    key: string;
    inMonth: boolean;
    isToday: boolean;
  };

  const calendarDays = $derived(buildCalendar(calendarMonth));
  const selectedDate = $derived(value ? parseLocalDateTimeInput(value) : null);
  const currentHour = $derived(value ? (parseLocalDateTimeInput(value)?.getHours() ?? 0) : 0);
  const currentMinute = $derived(
    value ? (parseLocalDateTimeInput(value)?.getMinutes() ?? 0) : 0,
  );
  const currentSecond = $derived(
    value ? (parseLocalDateTimeInput(value)?.getSeconds() ?? 0) : 0,
  );
  const descriptionIds = $derived(
    [error ? errorId : null, hint ? hintId : null].filter(Boolean).join(' ') || undefined,
  );

  $effect(() => {
    if (value !== lastValue) {
      lastValue = value;
      if (document.activeElement !== inputElement) {
        text = value ? toDisplayDateTime(value) : '';
        error = null;
      }
    }
  });

  function valueDate(): Date | null {
    return value ? parseLocalDateTimeInput(value) : null;
  }

  function emit(next: string | null) {
    if (next !== value) {
      value = next;
      onchange?.(next);
    }
  }

  function handleInput(event: Event) {
    text = (event.currentTarget as HTMLInputElement).value;
    error = null;

    if (text.trim() === '') {
      emit(null);
      return;
    }

    const parsed = parseDisplayDateTime(text);
    emit(parsed ? toLocalDateTimeInput(parsed) : null);
  }

  function handleBlur() {
    const trimmed = text.trim();

    if (trimmed === '') {
      if (required) {
        error = 'Choose a date and time.';
      }
      emit(null);
      return;
    }

    const parsed = parseDisplayDateTime(trimmed);
    if (!parsed) {
      error = 'Use dd-mm-yyyy HH:mm:ss.';
      emit(null);
      return;
    }

    error = null;
    commit(parsed);
  }

  function commit(date: Date) {
    text = toDisplayDateTime(date);
    error = null;
    emit(toLocalDateTimeInput(date));
  }

  function commitParts(
    base: Date,
    hours: number,
    minutes: number,
    seconds: number,
  ): boolean {
    const candidate = buildLocalDateTime(
      base.getFullYear(),
      base.getMonth() + 1,
      base.getDate(),
      hours,
      minutes,
      seconds,
    );

    if (!candidate) {
      error = 'That local time does not exist because of a daylight-saving change.';
      return false;
    }

    commit(candidate);
    return true;
  }

  async function openDatePicker() {
    if (disabled) {
      return;
    }

    if (dateOpen) {
      closePopovers();
      inputElement?.focus();
      return;
    }

    closePopovers();
    const base = valueDate() ?? new Date();
    calendarMonth = new SvelteDate(startOfMonth(base));
    dateOpen = true;
    await tick();
    if (!dateOpen) {
      return;
    }

    const target =
      datePopover?.querySelector<HTMLButtonElement>('.calendar-day.selected') ??
      datePopover?.querySelector<HTMLButtonElement>('.calendar-day.today') ??
      datePopover?.querySelector<HTMLButtonElement>('.calendar-day:not(.outside)') ??
      datePopover?.querySelector<HTMLButtonElement>('.calendar-day');
    target?.focus();
  }

  async function openTimePicker() {
    if (disabled) {
      return;
    }

    if (timeOpen) {
      closePopovers();
      inputElement?.focus();
      return;
    }

    const base = valueDate();
    closePopovers();
    if (!base) {
      commit(new Date());
    }
    timeOpen = true;
    await tick();
    if (timeOpen) {
      timePopover?.querySelector<HTMLInputElement>('input')?.focus();
    }
  }

  function closePopovers() {
    dateOpen = false;
    timeOpen = false;
  }

  function handleWindowPointerDown(event: PointerEvent) {
    if (root && !root.contains(event.target as Node)) {
      closePopovers();
    }
  }

  function handleWindowKeydown(event: KeyboardEvent) {
    if (event.key !== 'Escape' || !root) {
      return;
    }

    if (!root.contains(document.activeElement)) {
      return;
    }

    closePopovers();
    inputElement?.focus();
  }

  function isSelectedDay(date: Date): boolean {
    return selectedDate !== null && sameDay(selectedDate, date);
  }

  function isToday(date: Date): boolean {
    return sameDay(new Date(), date);
  }

  function sameDay(left: Date, right: Date): boolean {
    return (
      left.getFullYear() === right.getFullYear() &&
      left.getMonth() === right.getMonth() &&
      left.getDate() === right.getDate()
    );
  }

  function startOfMonth(date: Date): Date {
    return new Date(date.getFullYear(), date.getMonth(), 1);
  }

  function buildCalendar(month: Date): CalendarDay[] {
    const first = startOfMonth(month);
    const offset = (first.getDay() + 6) % 7;
    const start = new Date(first.getFullYear(), first.getMonth(), 1 - offset);

    return Array.from({ length: 42 }, (_, index) => {
      const date = new Date(
        start.getFullYear(),
        start.getMonth(),
        start.getDate() + index,
      );

      return {
        date,
        key: `${date.getFullYear()}-${date.getMonth()}-${date.getDate()}`,
        inMonth: date.getMonth() === month.getMonth(),
        isToday: isToday(date),
      };
    });
  }

  function shiftMonth(delta: number) {
    calendarMonth = new SvelteDate(
      calendarMonth.getFullYear(),
      calendarMonth.getMonth() + delta,
      1,
    );
  }

  function selectDay(date: Date) {
    const base = valueDate() ?? new Date();
    const committed = commitParts(
      date,
      base.getHours(),
      base.getMinutes(),
      base.getSeconds(),
    );

    if (committed) {
      dateOpen = false;
      inputElement?.focus();
    }
  }

  function selectToday() {
    selectDay(new Date());
  }

  function setTimePart(part: 'hours' | 'minutes' | 'seconds', raw: string) {
    if (raw.trim() === '') {
      return;
    }

    const parsed = Number(raw);
    if (!Number.isInteger(parsed)) {
      return;
    }

    const base = valueDate() ?? new Date();
    const hours = part === 'hours' ? clamp(parsed, 0, 23) : base.getHours();
    const minutes = part === 'minutes' ? clamp(parsed, 0, 59) : base.getMinutes();
    const seconds = part === 'seconds' ? clamp(parsed, 0, 59) : base.getSeconds();

    commitParts(base, hours, minutes, seconds);
  }

  function adjustTimePart(
    part: 'hours' | 'minutes' | 'seconds',
    delta: number,
  ) {
    const base = valueDate() ?? new Date();
    let hours = base.getHours();
    let minutes = base.getMinutes();
    let seconds = base.getSeconds();

    if (part === 'hours') {
      hours = wrap(hours + delta, 0, 23);
    } else if (part === 'minutes') {
      minutes = wrap(minutes + delta, 0, 59);
    } else {
      seconds = wrap(seconds + delta, 0, 59);
    }

    commitParts(base, hours, minutes, seconds);
  }

  function setNow() {
    commit(new Date());
  }

  function clamp(value: number, minimum: number, maximum: number): number {
    return Math.min(Math.max(value, minimum), maximum);
  }

  function wrap(value: number, minimum: number, maximum: number): number {
    if (value < minimum) {
      return maximum;
    }

    if (value > maximum) {
      return minimum;
    }

    return value;
  }

  function monthLabel(date: Date): string {
    return monthFormat.format(date);
  }

  function dayLabel(date: Date): string {
    return formatDateTime(date, false);
  }
</script>

<svelte:window onpointerdown={handleWindowPointerDown} onkeydown={handleWindowKeydown} />

<div
  class="date-time-field"
  class:has-error={error !== null}
  bind:this={root}
>
  <label for={inputId}>{label}</label>

  <div class="date-time-input-wrap">
    <input
      bind:this={inputElement}
      id={inputId}
      type="text"
      value={text}
      placeholder="dd-mm-yyyy HH:mm:ss"
      maxlength="19"
      autocomplete="off"
      spellcheck="false"
      {required}
      {disabled}
      aria-invalid={error ? 'true' : undefined}
      aria-describedby={descriptionIds}
      oninput={handleInput}
      onblur={handleBlur}
      onfocus={() => (error = null)}
    />

    <div class="date-time-actions">
      <button
        class="date-time-icon"
        type="button"
        aria-label="Choose date"
        aria-haspopup="dialog"
        aria-expanded={dateOpen}
        {disabled}
        onclick={openDatePicker}
      >
        <svg viewBox="0 0 20 20" aria-hidden="true">
          <rect x="3" y="4.5" width="14" height="12" rx="2"></rect>
          <path d="M3 8h14M7 2.5v4M13 2.5v4"></path>
        </svg>
      </button>

      <button
        class="date-time-icon"
        type="button"
        aria-label="Choose time"
        aria-haspopup="dialog"
        aria-expanded={timeOpen}
        {disabled}
        onclick={openTimePicker}
      >
        <svg viewBox="0 0 20 20" aria-hidden="true">
          <circle cx="10" cy="10" r="7.5"></circle>
          <path d="M10 5.5V10l3 2"></path>
        </svg>
      </button>
    </div>
  </div>

  {#if error}
    <p class="date-time-error" id={errorId}>{error}</p>
  {:else if hint}
    <p class="date-time-hint" id={hintId}>{hint}</p>
  {/if}

  {#if dateOpen}
    <div
      class="date-time-popover calendar-popover"
      role="dialog"
      aria-label="Choose date"
      bind:this={datePopover}
    >
      <div class="calendar-header">
        <button
          class="calendar-nav"
          type="button"
          aria-label="Previous month"
          onclick={() => shiftMonth(-1)}
        >
          ‹
        </button>
        <strong>{monthLabel(calendarMonth)}</strong>
        <button
          class="calendar-nav"
          type="button"
          aria-label="Next month"
          onclick={() => shiftMonth(1)}
        >
          ›
        </button>
      </div>

      <div class="calendar-weekdays" aria-hidden="true">
        {#each weekdays as weekday (weekday)}
          <span>{weekday}</span>
        {/each}
      </div>

      <div class="calendar-grid">
        {#each calendarDays as day (day.key)}
          <button
            class="calendar-day"
            class:outside={!day.inMonth}
            class:selected={isSelectedDay(day.date)}
            class:today={day.isToday}
            type="button"
            aria-label={dayLabel(day.date)}
            aria-pressed={isSelectedDay(day.date)}
            onclick={() => selectDay(day.date)}
          >
            {day.date.getDate()}
          </button>
        {/each}
      </div>

      <div class="calendar-footer">
        <button class="button small ghost" type="button" onclick={selectToday}>
          Today
        </button>
        <button
          class="button small ghost"
          type="button"
          onclick={() => {
            dateOpen = false;
            inputElement?.focus();
          }}
        >
          Close
        </button>
      </div>
    </div>
  {/if}

  {#if timeOpen}
    <div
      class="date-time-popover time-popover"
      role="dialog"
      aria-label="Choose time"
      bind:this={timePopover}
    >
      <div class="time-parts">
        {#each [
          { part: 'hours' as const, label: 'Hours', value: currentHour },
          { part: 'minutes' as const, label: 'Minutes', value: currentMinute },
          { part: 'seconds' as const, label: 'Seconds', value: currentSecond },
        ] as entry (entry.part)}
          <div class="time-part">
            <span>{entry.label.slice(0, 1)}</span>
            <button
              class="time-step"
              type="button"
              aria-label={`Increase ${entry.label.toLowerCase()}`}
              onclick={() => adjustTimePart(entry.part, 1)}
            >
              +
            </button>
            <input
              type="text"
              value={entry.value.toString().padStart(2, '0')}
              aria-label={entry.label}
              inputmode="numeric"
              maxlength="2"
              autocomplete="off"
              onchange={(event) =>
                setTimePart(
                  entry.part,
                  (event.currentTarget as HTMLInputElement).value,
                )}
              onblur={(event) => {
                (event.currentTarget as HTMLInputElement).value = entry.value
                  .toString()
                  .padStart(2, '0');
              }}
            />
            <button
              class="time-step"
              type="button"
              aria-label={`Decrease ${entry.label.toLowerCase()}`}
              onclick={() => adjustTimePart(entry.part, -1)}
            >
              −
            </button>
          </div>
        {/each}
      </div>

      <div class="calendar-footer">
        <button class="button small ghost" type="button" onclick={setNow}>
          Now
        </button>
        <button
          class="button small ghost"
          type="button"
          onclick={() => {
            timeOpen = false;
            inputElement?.focus();
          }}
        >
          Close
        </button>
      </div>
    </div>
  {/if}

</div>
