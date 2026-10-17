import { readFile } from 'node:fs/promises';
import { join } from 'node:path';

/**
 * Checks the plain-text views of the documentation that were built into `.next`.
 *
 * These files are what an agent reads instead of the site, and nothing about them is visible in a browser, so a defect
 * in them is invisible until someone fetches one. This gate asserts the properties that make them usable: the index
 * follows the llms.txt shape, every link in it is absolute and resolves to a Markdown endpoint that was actually
 * built, every entry carries a note, and the complete text holds every page the index lists with no relative link
 * left in it.
 */

const outputRoot = join(process.cwd(), '.next', 'server', 'app');
const indexPath = '/llms.txt';
const fullPath = '/llms-full.txt';
const docsBasePath = '/docs';
const siteUrl = 'https://litebus.io';
const failures = [];

/** The prerendered body of a route, or `undefined` when the route was not built. */
async function readBody(route) {
  try {
    return await readFile(join(outputRoot, `${route}.body`), 'utf8');
  } catch {
    return undefined;
  }
}

function fail(message) {
  failures.push(message);
}

/** Every Markdown link in a document, as `{ text, url }`. */
function markdownLinks(text) {
  return [...text.matchAll(/\[([^\]]*)\]\(([^)]+)\)/g)].map((match) => ({
    text: match[1],
    url: match[2],
  }));
}

/**
 * The route that serves a `.md` URL.
 *
 * Mirrors the rewrite in next.config.mjs, so a link in the index is checked against the file the rewrite would reach
 * rather than against the URL it was written as.
 */
function markdownRoute(url) {
  const path = url.slice(siteUrl.length).replace(/\.md(?:#.*)?$/, '');

  return `md${path}`;
}

const index = await readBody(indexPath.slice(1));
const full = await readBody(fullPath.slice(1));

if (index === undefined) {
  fail(`no index was built at ${indexPath}.`);
} else if (full === undefined) {
  fail(`no complete text was built at ${fullPath}.`);
} else {
  const lines = index.split('\n');
  const firstContent = lines.find((line) => line.trim().length > 0);

  // The convention requires an H1 naming the project, then a blockquote summary, then file lists under H2 headings.
  // Anything deeper than H2 would make a list ambiguous about which section it belongs to.
  if (firstContent === undefined || !firstContent.startsWith('# ')) {
    fail(`${indexPath} does not open with a level-one heading.`);
  }

  const headings = lines.filter((line) => /^#{1,6}\s/.test(line));
  const firstSection = lines.findIndex((line) => line.startsWith('## '));

  if (headings.filter((heading) => heading.startsWith('# ')).length !== 1) {
    fail(`${indexPath} has ${headings.length} level-one headings, and the convention allows one.`);
  }

  if (firstSection === -1) {
    fail(`${indexPath} has no level-two section.`);
  }

  if (!lines.slice(0, firstSection === -1 ? undefined : firstSection).some((line) => line.startsWith('> '))) {
    fail(`${indexPath} has no blockquote summary above its first section.`);
  }

  const deepHeading = headings.find((heading) => /^#{3,6}\s/.test(heading));

  if (deepHeading !== undefined) {
    fail(`${indexPath} has a heading below level two (${deepHeading.trim()}).`);
  }

  // Only the entries under a section are file lists. The prose above the first one may mention a URL without linking.
  const listedEntries = lines
    .slice(firstSection === -1 ? 0 : firstSection)
    .filter((line) => /^\s*-\s/.test(line));

  for (const entry of listedEntries) {
    const [link] = markdownLinks(entry);

    // A row with no link is a folder heading in the tree, which carries no URL of its own.
    if (link === undefined) {
      continue;
    }

    if (!link.url.startsWith(`${siteUrl}/`)) {
      fail(`${indexPath} links '${link.text}' to '${link.url}', which is not an absolute site URL.`);
      continue;
    }

    if (!/\.md(?:#|$)/.test(link.url)) {
      fail(`${indexPath} links '${link.text}' to '${link.url}', which is not a Markdown endpoint.`);
      continue;
    }

    if ((await readBody(markdownRoute(link.url))) === undefined) {
      fail(`${indexPath} links '${link.text}' to '${link.url}', which was not built.`);
    }

    // The note after the link is the page's description. A missing one means a page the description extraction in
    // lib/doc-summary.ts could not read, and an index entry that says nothing about the page it points at.
    if (!/\):\s\S/.test(entry)) {
      fail(`${indexPath} lists '${link.text}' with no note.`);
    }
  }

  const pageComments = [...full.matchAll(/^<!-- markdown: (\S+) -->$/gm)].map((match) => match[1]);

  // The contents list runs from its heading to the comment that opens the first page. Reading past that would collect
  // every link in every page instead.
  const contentsStart = full.indexOf('## Contents');
  const contentsEnd = full.indexOf('\n<!-- page: ', contentsStart);
  const contents = markdownLinks(
    full.slice(contentsStart, contentsEnd === -1 ? undefined : contentsEnd),
  ).map((link) => link.url);

  if (pageComments.length === 0) {
    fail(`${fullPath} contains no pages.`);
  }

  if (pageComments.length !== contents.length) {
    fail(
      `${fullPath} lists ${contents.length} pages in its contents but reproduces ${pageComments.length}.`,
    );
  }

  const missing = contents.filter((url) => !pageComments.includes(url));

  if (missing.length > 0) {
    fail(`${fullPath} lists pages it does not reproduce: ${missing.slice(0, 5).join(', ')}.`);
  }

  // A relative path resolves against nothing once the pages are concatenated, so one surviving here is a link that
  // silently leads nowhere. The rendered site has the same gate in check-rendered-links.mjs.
  const relative = [...full.matchAll(/\]\((\.{1,2}\/[^)]*)\)/g)].map((match) => match[1]);

  if (relative.length > 0) {
    fail(`${fullPath} has ${relative.length} unresolved relative links, such as '${relative[0]}'.`);
  }

  const base = docsBasePath;
  const foreign = pageComments.filter((url) => !url.startsWith(`${siteUrl}${base}/`) && url !== `${siteUrl}${base}.md`);

  if (foreign.length > 0) {
    fail(`${fullPath} reproduces pages outside ${base}: ${foreign.slice(0, 5).join(', ')}.`);
  }
}

if (failures.length > 0) {
  throw new Error(`The plain-text documentation views failed validation:\n  ${failures.join('\n  ')}`);
}

console.log('Plain-text documentation views validated.');
