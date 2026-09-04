import { readdir, readFile } from 'node:fs/promises';
import { join, relative } from 'node:path';

const outputRoot = join(process.cwd(), '.next', 'server', 'app');
const { versions } = JSON.parse(await readFile(join(process.cwd(), 'versions.json'), 'utf8'));

// The prerendered route for a version, without its leading slash: `docs` for the default version, `v7/docs` otherwise.
// Mirrors versionBasePath in lib/versions.ts.
const documentationRoutes = versions.map((version) => (version.default ? 'docs' : `${version.id}/docs`));
const renderedPages = (await findHtmlFiles(outputRoot)).filter((page) => {
  const route = relative(outputRoot, page).replaceAll('\\', '/');

  return documentationRoutes.some((root) => route === `${root}.html` || route.startsWith(`${root}/`));
});
const invalidLinks = [];
const renderedRoots = new Set();

for (const page of renderedPages) {
  const route = relative(outputRoot, page).replaceAll('\\', '/');
  const root = documentationRoutes.find((candidate) => route === `${candidate}.html` || route.startsWith(`${candidate}/`));
  renderedRoots.add(root);

  const html = await readFile(page, 'utf8');

  for (const match of html.matchAll(/href="([^"]+\.md(?:#[^"]*)?)"/gi)) {
    if (!/^https?:\/\//i.test(match[1])) {
      invalidLinks.push(`${route}: ${match[1]}`);
    }
  }
}

// Every declared version has to be prerendered. A version that silently stops building would otherwise pass this gate
// by contributing no pages to check.
const missingVersions = documentationRoutes.filter((root) => !renderedRoots.has(root));

if (missingVersions.length > 0) {
  throw new Error(
    `No prerendered documentation pages were found for: ${missingVersions.join(', ')} (under ${outputRoot}).`,
  );
}

if (invalidLinks.length > 0) {
  throw new Error(`Rendered documentation contains source-file links:\n${invalidLinks.join('\n')}`);
}

console.log(
  `Checked ${renderedPages.length} rendered documentation pages across ${documentationRoutes.length} versions.`,
);

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
