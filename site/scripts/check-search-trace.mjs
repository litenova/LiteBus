import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';

// The search route reads Markdown at request time, so the content has to reach the deployed bundle; a missing
// trace entry is documentation whose search returns nothing in production. Keep this in step with `docsContentDir`
// in lib/site.ts.
const contentRoots = ['content/docs'];
const traceFile = path.resolve('.next/server/app/api/search/route.js.nft.json');
const trace = JSON.parse(await readFile(traceFile, 'utf8'));

const tracedFiles = new Set(
  trace.files.map((file) => path.resolve(path.dirname(traceFile), file).replaceAll('\\', '/')),
);

const missingFiles = [];
let checked = 0;

for (const root of contentRoots) {
  const absoluteRoot = path.resolve(root);
  const entries = await readdir(absoluteRoot, { recursive: true, withFileTypes: true });

  for (const entry of entries) {
    if (!entry.isFile() || !entry.name.endsWith('.md')) {
      continue;
    }

    checked += 1;
    const sourceFile = path.join(entry.parentPath, entry.name).replaceAll('\\', '/');

    if (!tracedFiles.has(sourceFile)) {
      missingFiles.push(path.relative(process.cwd(), sourceFile).replaceAll('\\', '/'));
    }
  }
}

if (checked === 0) {
  console.error(`No documentation source files were found under ${contentRoots.join(', ')}.`);
  process.exitCode = 1;
} else if (missingFiles.length > 0) {
  console.error('The search route trace is missing documentation source files:');
  for (const file of missingFiles.sort()) {
    console.error(`- ${file}`);
  }

  process.exitCode = 1;
} else {
  console.log(`Verified ${checked} documentation files in the search route trace.`);
}
