import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';

/**
 * Checks the documentation naming model against the Markdown sources.
 *
 * A page has one name, its H1, and a folder label is that same name on the folder's index page, so the sidebar,
 * breadcrumb, tab title, search result, and plain-text entry for a page can all show one text. This gate asserts the
 * two rules that live in the sources: each folder label matches its index page H1, and page names are unique within a
 * top-level section. It prints one line per violation and exits 1 when any fail.
 */

const docsRoot = join(process.cwd(), 'content', 'docs');
const violations = [];

/** The text of the first level-one heading, mirroring extractTitle in lib/doc-summary.ts. */
function extractTitle(content) {
  const text = content.charCodeAt(0) === 0xfeff ? content.slice(1) : content;

  return /^#\s+(.+?)\s*$/m.exec(text)?.[1];
}

/** Every directory under a root, including the root itself. */
function walkDirectories(root) {
  const directories = [root];

  for (const entry of readdirSync(root, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      directories.push(...walkDirectories(join(root, entry.name)));
    }
  }

  return directories;
}

/** Every Markdown source under a root, as paths relative to the docs root with forward slashes. */
function walkPages(root) {
  const pages = [];

  for (const directory of walkDirectories(root)) {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      if (entry.isFile() && entry.name.endsWith('.md')) {
        pages.push(relative(root, join(directory, entry.name)).replaceAll('\\', '/'));
      }
    }
  }

  return pages.sort();
}

/** The top-level section a page belongs to: its first path segment, or the root for the index page. */
function sectionOf(page) {
  const segment = page.split('/')[0];

  return segment === 'index.md' ? '(root)' : segment;
}

if (!existsSync(docsRoot)) {
  console.error(`Documentation content was not found at ${docsRoot}.`);
  process.exitCode = 1;
} else {
  // A folder label equals the H1 of the folder index page when the folder has one.
  for (const directory of walkDirectories(docsRoot).sort()) {
    const metaPath = join(directory, 'meta.json');

    if (!existsSync(metaPath)) {
      continue;
    }

    const label = JSON.parse(readFileSync(metaPath, 'utf8')).title;

    if (typeof label !== 'string' || label.length === 0) {
      continue;
    }

    const index = ['README.md', 'index.md']
      .map((name) => join(directory, name))
      .find((candidate) => existsSync(candidate));

    if (index === undefined) {
      continue;
    }

    const page = relative(docsRoot, index).replaceAll('\\', '/');
    const title = extractTitle(readFileSync(index, 'utf8'));

    if (title === undefined) {
      violations.push(`folder index "${page}" has no level-one heading to match its "${label}" label.`);
    } else if (title !== label) {
      violations.push(`folder "${relative(docsRoot, directory).replaceAll('\\', '/') || '(root)'}" is labelled "${label}" but its index page "${page}" is named "${title}".`);
    }
  }

  // Page names are unique within a top-level section.
  const namesBySection = new Map();

  for (const page of walkPages(docsRoot)) {
    const title = extractTitle(readFileSync(join(docsRoot, page), 'utf8'));

    if (title === undefined) {
      continue;
    }

    const section = sectionOf(page);

    if (!namesBySection.has(section)) {
      namesBySection.set(section, new Map());
    }

    const names = namesBySection.get(section);

    if (!names.has(title)) {
      names.set(title, []);
    }

    names.get(title).push(page);
  }

  for (const [section, names] of [...namesBySection.entries()].sort(([a], [b]) => (a < b ? -1 : 1))) {
    for (const [title, pages] of [...names.entries()].sort(([a], [b]) => (a < b ? -1 : 1))) {
      if (pages.length > 1) {
        violations.push(`page name "${title}" is not unique in section "${section}": ${pages.join(', ')}.`);
      }
    }
  }
}

if (violations.length > 0) {
  for (const violation of violations.sort()) {
    console.error(violation);
  }

  process.exitCode = 1;
} else {
  console.log('Documentation titles validated.');
}
