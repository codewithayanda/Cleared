import { readFileSync } from 'node:fs';

const ALLOWED_HOST = 'registry.npmjs.org';
const LOCKFILE = new URL('../package-lock.json', import.meta.url);

const lock = readFileSync(LOCKFILE, 'utf8');
const resolved = [...lock.matchAll(/"resolved":\s*"https?:\/\/([^/"]+)/g)].map((m) => m[1]);
const hosts = new Set(resolved);
const offenders = [...hosts].filter((host) => host !== ALLOWED_HOST);

if (offenders.length > 0) {
  console.error('package-lock.json resolves packages from a non-public registry:\n');
  for (const host of offenders) {
    console.error(`  - ${host}`);
  }
  console.error(`\nOnly ${ALLOWED_HOST} is permitted.`);
  console.error('A private or corporate feed cannot be reached by CI, and its URLs must');
  console.error('not be committed to this repository.\n');
  console.error('Fix: confirm client/.npmrc pins the public registry, then');
  console.error('  rm -rf node_modules package-lock.json && npm install');
  process.exit(1);
}

console.log(
  `package-lock.json: ${resolved.length} resolved URLs, all from ${[...hosts].join(', ')} — OK`,
);
