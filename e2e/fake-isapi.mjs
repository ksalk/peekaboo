import http from 'node:http';

const port = Number(process.env.PEEKABOO_FAKE_ISAPI_PORT ?? 8666);
const username = process.env.CAMERA_USERNAME ?? 'camuser';
const password = process.env.CAMERA_PASSWORD ?? 'campassword';
const expectedAuthorization = `Basic ${Buffer.from(`${username}:${password}`).toString('base64')}`;
const maximumBodyBytes = 256 * 1024;
const deviceOffsetMinutes = Number(
  process.env.PEEKABOO_FAKE_NVR_OFFSET_MINUTES ?? '60',
);

const server = http.createServer((request, response) => {
  void handle(request, response).catch((error) => {
    console.error('fake ISAPI handler failed', error);

    if (!response.headersSent) {
      response.writeHead(500);
      response.end('internal error');
    } else {
      response.destroy();
    }
  });
});

server.on('error', (error) => {
  console.error('fake ISAPI server error', error);
  process.exit(1);
});

async function handle(request, response) {
  console.log(`${request.method} ${request.url}`);

  if (request.headers.authorization !== expectedAuthorization) {
    response.writeHead(401, {
      'WWW-Authenticate': 'Basic realm="peekaboo-e2e"',
      'Content-Type': 'text/plain',
    });
    response.end('unauthorized');
    return;
  }

  if (request.method === 'GET' && request.url === '/ISAPI/System/deviceInfo') {
    response.writeHead(200, { 'Content-Type': 'application/xml' });
    response.end(
      '<?xml version="1.0" encoding="UTF-8"?>' +
        '<DeviceInfo><deviceName>Peekaboo E2E NVR</deviceName></DeviceInfo>',
    );
    return;
  }

  if (request.method === 'GET' && request.url === '/ISAPI/System/time') {
    const offsetSign = deviceOffsetMinutes >= 0 ? '+' : '-';
    const offsetHours = Math.floor(Math.abs(deviceOffsetMinutes) / 60)
      .toString()
      .padStart(2, '0');
    const offsetMinutesPart = (Math.abs(deviceOffsetMinutes) % 60)
      .toString()
      .padStart(2, '0');
    const localTime = new Date(Date.now() + deviceOffsetMinutes * 60 * 1000)
      .toISOString()
      .replace(/\.\d{3}Z$/, `${offsetSign}${offsetHours}:${offsetMinutesPart}`);

    response.writeHead(200, { 'Content-Type': 'application/xml' });
    response.end(
      '<?xml version="1.0" encoding="UTF-8"?>' +
        '<Time><timeMode>manual</timeMode>' +
        `<localTime>${localTime}</localTime>` +
        '<timeZone>CET-1CEST,M3.5.0/2,M10.5.0/3</timeZone></Time>',
    );
    return;
  }

  if (request.method === 'POST' && request.url === '/ISAPI/ContentMgmt/search') {
    const body = await readBody(request);
    const trackId = body.match(/<trackID>(\d+)<\/trackID>/)?.[1] ?? '101';
    const startValue = body.match(/<startTime>(.*?)<\/startTime>/)?.[1];
    const endValue = body.match(/<endTime>(.*?)<\/endTime>/)?.[1];
    const position = Number(
      body.match(/<searchResultPostion>(\d+)<\/searchResultPostion>/)?.[1] ?? '0',
    );

    response.writeHead(200, { 'Content-Type': 'application/xml' });

    if (!startValue || !endValue) {
      response.end(resultXml(trackId, 'NO MATCHES', 0, []));
      return;
    }

    const start = parseDeviceTime(startValue);
    const end = parseDeviceTime(endValue);

    if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime()) || end <= start) {
      response.end(resultXml(trackId, 'NO MATCHES', 0, []));
      return;
    }

    const range = end.getTime() - start.getTime();

    if (position === 0) {
      const segmentStart = new Date(
        start.getTime() + Math.min(range / 5, 60 * 60 * 1000),
      );
      const segmentEnd = new Date(
        Math.min(end.getTime(), segmentStart.getTime() + 5 * 60 * 1000),
      );

      response.end(
        resultXml(trackId, 'MORE', 1, [{ start: segmentStart, end: segmentEnd }]),
      );
      return;
    }

    if (position === 1) {
      const segmentStart = new Date(
        start.getTime() + Math.min(range / 2, 2 * 60 * 60 * 1000),
      );
      const segmentEnd = new Date(
        Math.min(end.getTime(), segmentStart.getTime() + 5 * 60 * 1000),
      );

      if (segmentEnd > segmentStart) {
        response.end(
          resultXml(trackId, 'OK', 1, [{ start: segmentStart, end: segmentEnd }]),
        );
        return;
      }
    }

    response.end(resultXml(trackId, 'NO MATCHES', 0, []));
    return;
  }

  if (request.method === 'POST' && request.url === '/ISAPI/ContentMgmt/logSearch') {
    const body = await readBody(request);
    const metaId = body.match(/<metaId>(.*?)<\/metaId>/)?.[1];
    const startValue = body.match(/<startTime>(.*?)<\/startTime>/)?.[1];
    const endValue = body.match(/<endTime>(.*?)<\/endTime>/)?.[1];
    const position = Number(
      body.match(/<searchResultPostion>(\d+)<\/searchResultPostion>/)?.[1] ?? '0',
    );

    if (metaId !== 'log.std-cgi.com/Alarm') {
      response.writeHead(400, { 'Content-Type': 'application/xml' });
      response.end(
        '<?xml version="1.0" encoding="UTF-8"?>' +
          '<ResponseStatus><statusCode>4</statusCode>' +
          '<statusString>Invalid metaId</statusString></ResponseStatus>',
      );
      return;
    }

    response.writeHead(200, { 'Content-Type': 'application/xml' });

    if (!startValue || !endValue) {
      response.end(logResultXml('NO MATCHES', 0, []));
      return;
    }

    const start = parseDeviceTime(startValue);
    const end = parseDeviceTime(endValue);

    if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime()) || end <= start) {
      response.end(logResultXml('NO MATCHES', 0, []));
      return;
    }

    const range = end.getTime() - start.getTime();
    const midpoint = new Date(start.getTime() + range / 2);

    if (position === 0) {
      response.end(
        logResultXml('MORE', 2, [
          {
            kind: 'motionStart',
            time: midpoint,
            channel: 1,
            target: 'Osoba',
          },
          {
            kind: 'motionStart',
            time: new Date(midpoint.getTime() + 10_000),
            channel: 1,
            target: 'Pojazd',
          },
        ]),
      );
      return;
    }

    if (position === 2) {
      response.end(
        logResultXml('OK', 2, [
          {
            kind: 'motionStop',
            time: new Date(midpoint.getTime() + 7_000),
            channel: 1,
            target: 'Osoba',
          },
          {
            kind: 'motionStop',
            time: new Date(midpoint.getTime() + 17_000),
            channel: 1,
            target: 'Pojazd',
          },
        ]),
      );
      return;
    }

    response.end(logResultXml('NO MATCHES', 0, []));
    return;
  }

  response.writeHead(404, { 'Content-Type': 'text/plain' });
  response.end('not found');
}

function resultXml(trackId, status, numOfMatches, segments) {
  const matches = segments
    .map(
      (segment) => `
        <searchMatchItem>
          <trackID>${trackId}</trackID>
          <timeSpan>
            <startTime>${formatTime(segment.start)}</startTime>
            <endTime>${formatTime(segment.end)}</endTime>
          </timeSpan>
          <mediaSegmentDescriptor>
            <contentType>video</contentType>
            <codecType>H.264-BP</codecType>
            <playbackURI>rtsp://127.0.0.1:8555/ISAPI/Streaming/tracks/${trackId}</playbackURI>
          </mediaSegmentDescriptor>
        </searchMatchItem>`,
    )
    .join('');

  return `<?xml version="1.0" encoding="UTF-8"?>
<CMSearchResult version="2.0" xmlns="http://www.isapi.com/ver20/XMLSchema">
  <searchID>{E2E-SEARCH}</searchID>
  <responseStatus>true</responseStatus>
  <responseStatusStrg>${status}</responseStatusStrg>
  <numOfMatches>${numOfMatches}</numOfMatches>
  <matchList>${matches}
  </matchList>
</CMSearchResult>`;
}

function logResultXml(status, numOfMatches, entries) {
  const matches = entries
    .map(
      (entry) => `
        <searchMatchItem>
          <logDescriptor>
            <metaId>log.hikvision.com/Alarm/${entry.kind}/${entry.channel}</metaId>
            <StartDateTime>${formatTime(entry.time)}</StartDateTime>
            <localId>D${entry.channel}</localId>
            <localID>D${entry.channel}</localID>
            <additionInformation>${entry.target}</additionInformation>
          </logDescriptor>
        </searchMatchItem>`,
    )
    .join('');

  return `<?xml version="1.0" encoding="UTF-8"?>
<CMSearchResult version="1.0" xmlns="http://www.hikvision.com/ver20/XMLSchema">
  <searchID>{E2E-LOG-SEARCH}</searchID>
  <responseStatus>true</responseStatus>
  <responseStatusStrg>${status}</responseStatusStrg>
  <numOfMatches>${numOfMatches}</numOfMatches>
  <matchList>${matches}
  </matchList>
</CMSearchResult>`;
}

function formatTime(value) {
  const shifted = new Date(value.getTime() + deviceOffsetMinutes * 60 * 1000);
  return shifted.toISOString().replace(/\.\d{3}Z$/, 'Z');
}

function parseDeviceTime(value) {
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return parsed;
  }

  return new Date(parsed.getTime() - deviceOffsetMinutes * 60 * 1000);
}

function readBody(request) {
  return new Promise((resolve, reject) => {
    let body = '';
    let size = 0;

    request.setEncoding('utf8');
    request.on('data', (chunk) => {
      size += chunk.length;

      if (size > maximumBodyBytes) {
        reject(new Error('request body too large'));
        request.destroy();
        return;
      }

      body += chunk;
    });
    request.on('end', () => resolve(body));
    request.on('error', reject);
  });
}

server.listen(port, '127.0.0.1', () => {
  console.log(`fake ISAPI listening on 127.0.0.1:${port}`);
});

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => {
    server.closeAllConnections?.();
    server.close(() => process.exit(0));
    setTimeout(() => process.exit(0), 1000).unref();
  });
}
