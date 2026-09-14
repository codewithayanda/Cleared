// Enforces the comment conventions in CLAUDE.md, so the house voice doesn't drift back.
// Run: node scripts/check-comment-style.mjs

import { readFileSync, readdirSync } from 'node:fs';
import { dirname, extname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');

const SKIP_DIRS = new Set([
  'node_modules',
  '.git',
  '.vs',
  '.angular',
  'obj',
  'bin',
  'dist',
  'coverage',
]);

const SCAN = [
  { dir: 'server', exts: ['.cs'] },
  { dir: 'client/src', exts: ['.ts', '.html', '.css'] },
  { dir: '.github', exts: ['.yml'] },
];

const EXTRA_FILES = ['README.md', 'CLAUDE.md', 'client/README.md'];

// EF Core writes these; they are not hand-authored.
const GENERATED = /(\.Designer\.cs|ModelSnapshot\.cs)$/;

const EM_DASH = '—';
const MAX_COMMENT_LINES = 4;

function walk(dir, exts, out) {
  let entries;
  try {
    entries = readdirSync(dir, { withFileTypes: true });
  } catch {
    return;
  }

  for (const entry of entries) {
    if (entry.isDirectory()) {
      if (!SKIP_DIRS.has(entry.name)) walk(join(dir, entry.name), exts, out);
    } else if (exts.includes(extname(entry.name)) && !GENERATED.test(entry.name)) {
      out.push(join(dir, entry.name));
    }
  }
}

const files = [];
for (const { dir, exts } of SCAN) walk(join(root, dir), exts, files);
for (const file of EXTRA_FILES) files.push(join(root, file));

const problems = [];

for (const file of files) {
  let text;
  try {
    text = readFileSync(file, 'utf8');
  } catch {
    continue;
  }

  const rel = relative(root, file).replaceAll('\\', '/');
  const isTemplate = extname(file) === '.html';
  const lines = text.split(/\r?\n/);

  let run = 0;
  let runStart = 0;

  const flagLongRun = () => {
    if (run > MAX_COMMENT_LINES) {
      problems.push(`${rel}:${runStart}  ${run}-line comment block (max ${MAX_COMMENT_LINES})`);
    }
    run = 0;
  };

  lines.forEach((line, index) => {
    if (line.includes(EM_DASH)) {
      // A bare em dash standing in for an empty value in a template cell is intended
      // typography, not prose. Anything else is prose.
      const withoutPlaceholders = line.replaceAll(`'${EM_DASH}'`, '');
      if (!isTemplate || withoutPlaceholders.includes(EM_DASH)) {
        problems.push(`${rel}:${index + 1}  em dash`);
      }
    }

    if (/^\s*\/\//.test(line)) {
      if (run === 0) runStart = index + 1;
      run += 1;
    } else {
      flagLongRun();
    }
  });

  flagLongRun();
}

if (problems.length > 0) {
  console.error(`Comment style: ${problems.length} problem(s). See CLAUDE.md.\n`);
  for (const problem of problems) console.error(`  ${problem}`);
  process.exit(1);
}

console.log(`Comment style OK (${files.length} files scanned).`);
