import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';

const docsDirectory = path.resolve('content/docs');
const traceFile = path.resolve('.next/server/app/api/search/route.js.nft.json');
const trace = JSON.parse(await readFile(traceFile, 'utf8'));

const sourceFiles = (await readdir(docsDirectory, { recursive: true, withFileTypes: true }))
  .filter((entry) => entry.isFile() && entry.name.endsWith('.md'))
  .map((entry) => path.relative(docsDirectory, path.join(entry.parentPath, entry.name)).replaceAll('\\', '/'))
  .sort();

const tracedFiles = trace.files
  .map((file) => file.replaceAll('\\', '/'))
  .filter((file) => file.includes('/content/docs/') && file.endsWith('.md'))
  .map((file) => file.slice(file.indexOf('/content/docs/') + '/content/docs/'.length))
  .sort();

const missingFiles = sourceFiles.filter((file) => !tracedFiles.includes(file));

if (missingFiles.length > 0) {
  console.error('The search route trace is missing documentation source files:');
  for (const file of missingFiles) {
    console.error(`- ${file}`);
  }

  process.exitCode = 1;
} else {
  console.log(`Verified ${sourceFiles.length} documentation files in the search route trace.`);
}
