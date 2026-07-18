import { readdir, readFile } from 'node:fs/promises';
import { join, relative } from 'node:path';

const outputRoot = join(process.cwd(), '.next', 'server', 'app', 'docs');
const renderedPages = await findHtmlFiles(outputRoot);
const invalidLinks = [];

for (const page of renderedPages) {
  const html = await readFile(page, 'utf8');

  for (const match of html.matchAll(/href="([^"]+\.md(?:#[^"]*)?)"/gi)) {
    if (!/^https?:\/\//i.test(match[1])) {
      invalidLinks.push(`${relative(outputRoot, page)}: ${match[1]}`);
    }
  }
}

if (renderedPages.length === 0) {
  throw new Error(`No prerendered documentation pages were found under ${outputRoot}.`);
}

if (invalidLinks.length > 0) {
  throw new Error(`Rendered documentation contains source-file links:\n${invalidLinks.join('\n')}`);
}

console.log(`Checked ${renderedPages.length} rendered documentation pages.`);

async function findHtmlFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = [];

  for (const entry of entries) {
    const path = join(directory, entry.name);

    if (entry.isDirectory()) {
      files.push(...(await findHtmlFiles(path)));
    } else if (entry.isFile() && entry.name.endsWith('.html')) {
      files.push(path);
    }
  }

  return files;
}
