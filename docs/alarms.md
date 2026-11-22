# Motion alarms

Peekaboo can read the NVR's own alarm log over ISAPI and turn Hikvision
`motionStart` / `motionStop` entries into motion ranges.

This is deliberately different from the ISAPI alert stream:

- No long-lived event connection is opened.
- No alarm history is stored in Peekaboo's database.
- The NVR is queried only when someone opens playback or searches the Alarms page.
- Raw log results are cached in memory for 30 seconds.

The trade-off is that history is limited by the NVR's own log retention. If the NVR
rotates or deletes its logs, Peekaboo cannot show older alarms.

## NVR requirements

- Motion detection must be enabled and armed on the camera or NVR channel.
- The NVR must retain alarm logs.
- The NVR account must be allowed to read the log. A normal viewer account may already
  have this right; grant log-search/remote-log access in the NVR's user management if
  it does not.
- The alert-stream "Notify Surveillance Center" linkage is **not** required for this
  mode.
- For SDK-transport NVRs, the NVR's HTTP(S) ISAPI service must still be reachable.
  Live view and playback can continue over the SDK while alarms are read over ISAPI.

## ISAPI request

Peekaboo posts a `CMSearchDescription` to:

```http
POST /ISAPI/ContentMgmt/logSearch
Content-Type: application/xml
```

Example:

```xml
<CMSearchDescription version="1.0">
  <searchID>812F04E0-4089-11A3-9A0C-0305E82C2906</searchID>
  <timeSpanList>
    <timeSpan>
      <startTime>2026-11-21T00:00:00Z</startTime>
      <endTime>2026-11-22T00:00:00Z</endTime>
    </timeSpan>
  </timeSpanList>
  <metaId>log.std-cgi.com/Alarm</metaId>
  <maxResults>100</maxResults>
  <searchResultPostion>0</searchResultPostion>
</CMSearchDescription>
```

Protocol details:

- The element is **`<metaId>`**, not `<metaID>`. The capital `D` makes some firmware
  return a misleading `badXmlContent` / “two root tags” error.
- The firmware expects the misspelled **`<searchResultPostion>`**.
- Times are UTC ISO 8601.
- `log.std-cgi.com/Alarm` returns the major alarm log. Peekaboo keeps only
  `motionStart` and `motionStop`; other alarm subtypes are ignored.
- The NVR may return fewer rows than `maxResults` (often 64). Pagination advances by
  the number of rows the device actually returned, not by the requested page size.
- A search stops at 10,000 motion entries and has a two-minute upstream deadline. The
  Alarms page marks a partial result.

## Pairing motion starts and stops

The NVR logs starts and stops separately. Peekaboo:

1. Groups entries by NVR channel.
2. Pairs each `motionStart` with the next `motionStop` for the same channel and target.
3. Falls back to the earliest open start if the target label differs or is missing.
4. Expands the NVR query by 5 minutes on each side so motion crossing a boundary can be
   paired, then clips ranges to the requested window.
5. Marks a range `estimated start` when only a stop was found in the expanded window,
   and `estimated end` when motion was still open at the window edge.

Target labels are localized by the NVR. Known labels such as `Osoba` / `Person` /
`Human` become **person**, and `Pojazd` / `Vehicle` / `Car` become **vehicle**.
Anything else is shown as generic **motion**.

## API

```http
GET /api/alarms
  ?cameraId=<camera id>
  &nvrId=<nvr id>
  &start=<ISO 8601>
  &end=<ISO 8601>
  &target=human|vehicle|unknown
```

- `cameraId` and `nvrId` are optional. Omitting both searches every configured camera.
- If both are present, the camera must belong to that NVR.
- When searching every camera, an unreachable NVR does not hide alarms from the healthy
  NVRs. Its failure is listed in `errors` and the request still returns `200`.
- `start` and `end` are required. The range may span at most 31 days.
- `target` is `any` by default. `unknown` means unclassified motion only, not “any”.
- Results are newest first.

Response:

```json
{
  "start": "2026-11-21T00:00:00+00:00",
  "end": "2026-11-22T00:00:00+00:00",
  "truncated": false,
  "errors": [],
  "ranges": [
    {
      "cameraId": "…",
      "cameraName": "Front",
      "nvrId": "…",
      "nvrName": "House NVR",
      "channelId": 1,
      "start": "2026-11-21T09:44:55+00:00",
      "end": "2026-11-21T09:45:02+00:00",
      "durationSeconds": 7,
      "target": "vehicle",
      "targetLabel": "Pojazd",
      "inferredStart": false,
      "inferredEnd": false
    }
  ]
}
```

All authenticated users can read alarm logs, matching the existing NVR/camera
visibility model.

## UI

- **Playback** draws a merged **Motion** lane above recording coverage. Person,
  vehicle, and unclassified motion use different colours.
- **Alarms** (`#/alarms`) has camera, target, and time filters, with Today, Yesterday,
  and Last 7 days shortcuts. It defaults to the last 24 hours.
- Alarm rows show the camera, NVR channel, target, duration, and estimated-boundary
  badges.

## Troubleshooting

- **HTTP 403 or 401** — the NVR account cannot read the log. Grant log-search
  permission in the NVR's user management.
- **HTTP 404 / not supported** — the firmware does not expose
  `/ISAPI/ContentMgmt/logSearch`, or the NVR's ISAPI service is disabled.
- **No alarms but recording works** — enable motion detection and its arming schedule;
  check that the NVR retains alarm logs. Motion does not need to be linked to Notify
  Surveillance Center for this feature, but it does need to be detected.
- **Times are shifted** — check the NVR clock, timezone, and NTP settings. Peekaboo
  treats the log's `StartDateTime` as UTC.
- **Alarms have no camera** — the event channel is mapped with the camera's main
  stream ID (`101` → channel 1, `201` → channel 2). Check the stream ID on the NVRs
  page.
- **Only generic motion** — the NVR log text did not contain a recognized person or
  vehicle label. Non-AcuSense cameras may only report generic motion.

## Privacy

Motion alarms reveal occupancy and movement patterns even though Peekaboo does not
store them. Keep NVR log access restricted, use TLS, and do not give accounts to people
who should not see camera activity.
