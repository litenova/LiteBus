// Publishes the repository Changelog.md as the documentation page /docs/changelog.
//
// The root Changelog.md is the one source: release tooling reads it, and the site shows it. This copies it into the
// content folder before the site builds or serves, so the page goes through the same navigation, search, tab title and
// plain-text views as every other page. The copy is ignored by git and must not be edited.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const siteDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const source = path.resolve(siteDir, '..', 'Changelog.md');
const target = path.join(siteDir, 'content', 'docs', 'changelog.md');

const repositoryBlobUrl = 'https://github.com/litenova/LiteBus/blob/v7';

// Changelog.md links documentation pages by their repository path and other files relative to the repository root.
// From the docs folder, a documentation link becomes a sibling link and anything else becomes a link to the file on GitHub.
const text = fs
  .readFileSync(source, 'utf8')
  .replace(/\]\(site\/content\/docs\/([^)]+)\)/g, '](./$1)')
  .replace(/\]\((?!https?:|#|\.\/|mailto:)([^)]+)\)/g, `](${repositoryBlobUrl}/$1)`);

if (!/^# Changelog\s*$/m.test(text)) {
  throw new Error(`${source} must open with a "# Changelog" heading, which names the documentation page.`);
}

if (!fs.existsSync(target) || fs.readFileSync(target, 'utf8') !== text) {
  fs.writeFileSync(target, text);
  console.log(`Synced ${path.relative(siteDir, source)} to ${path.relative(siteDir, target)}.`);
}
