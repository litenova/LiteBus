// Checks the built static site in dist/.
//
// The build writes HTML, plain-text views, a sitemap, and redirect files that no browser pass covers completely,
// so this gate asserts the properties the site promises: every internal link resolves to a built file with its hash
// target present, no rendered link still points at a Markdown source except the "View Markdown" and agent links, the
// llms.txt index follows the llms.txt shape with absolute resolving links and a note per entry, llms-full.txt
// reproduces every indexed page with no relative link left, every page carries one h1, title, canonical link, and
// short meta description with unique titles, the canonical link and og:url name the page's clean URL, no breadcrumb
// list names the same URL twice, the sitemap lists every page and nothing unbuilt, and every redirect
// target resolves. It also keeps the source naming model: each folder label matches its index page H1, and page
// titles stay unique within a top-level section.
//
// Run from the site directory: `pnpm check`.
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join, posix } from 'node:path';

const siteUrl = 'https://litebus.io';
const distRoot = join(process.cwd(), 'dist');
const docsRoot = join(process.cwd(), 'content', 'docs');
const failures = [];

function fail(message) {
  failures.push(message);
}

/** Every file under a directory, as POSIX paths relative to it, or an empty list when it is missing. */
function walkFiles(root) {
  const found = [];

  function walk(directory) {
    let entries;

    try {
      entries = readdirSync(directory === '' ? root : join(root, directory), { withFileTypes: true });
    } catch {
      return;
    }

    for (const entry of entries) {
      const relative = directory === '' ? entry.name : `${directory}/${entry.name}`;

      if (entry.isDirectory()) {
        walk(relative);
      } else if (entry.isFile()) {
        found.push(relative);
      }
    }
  }

  walk('');

  return found.sort();
}

/** The text of a file, or undefined when it was not built. */
function readBuiltFile(relativePath) {
  try {
    return readFileSync(join(distRoot, relativePath), 'utf8');
  } catch {
    return undefined;
  }
}

/**
 * The dist file a site path serves, or undefined when nothing is built there.
 *
 * A path serves either the file at its own location (`/og.png` renders `og.png`, `/docs.md` renders `docs.md`) or,
 * for a page URL, that path with `.html` (`/docs/getting-started` renders `docs/getting-started.html`, `/` renders
 * `index.html`). Anything else, including a trailing-slash page URL, resolves to nothing: page URLs never end with
 * a slash except the home page.
 */
function resolveBuiltFile(relativePath, builtFiles) {
  const candidates = relativePath === '' ? ['index.html'] : [relativePath, `${relativePath}.html`];

  for (const candidate of candidates) {
    if (builtFiles.has(candidate)) {
      return candidate;
    }
  }

  try {
    const decoded = decodeURIComponent(relativePath);

    if (decoded !== relativePath) {
      for (const candidate of decoded === '' ? ['index.html'] : [decoded, `${decoded}.html`]) {
        if (builtFiles.has(candidate)) {
          return candidate;
        }
      }
    }
  } catch {
    // A path that is not valid percent-encoding resolves by its literal form only, which already failed.
  }

  return undefined;
}

/** Matches a link that already names a scheme, including `mailto:` and protocol-relative forms. */
const absoluteHrefPattern = /^(?:[a-z][a-z0-9+.-]*:|\/\/)/i;

/** Every Markdown link in a document, as `{ text, url }`. */
function markdownLinks(text) {
  return [...text.matchAll(/\[([^\]]*)\]\(([^)]+)\)/g)].map((match) => ({
    text: match[1],
    url: match[2],
  }));
}

/** A document with its fenced code blocks removed, so example syntax inside them is not checked as content. */
function withoutCodeFences(text) {
  return text.replace(/^(```|~~~).*$\n[\s\S]*?^(?:\1).*$/gm, '');
}

/** Plain text from an HTML fragment, for anchor text and head element comparisons. */
function plainText(html) {
  return html
    .replace(/<[^>]*>/g, '')
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#39;|&#x27;/g, "'")
    .replace(/\s+/g, ' ')
    .trim();
}

const builtFiles = new Set(walkFiles(distRoot));
const htmlFiles = [...builtFiles].filter(
  (file) => file.endsWith('.html') && !file.startsWith('pagefind/'),
);
const pageIds = new Map();
const pageTitles = new Map();
let checkedLinks = 0;

// The documentation has to be built. Pages that silently stop building would otherwise pass this gate by
// contributing nothing to check.
if (htmlFiles.length === 0) {
  fail(`no HTML pages were built under ${distRoot}. Run \`pnpm build\` before \`pnpm check\`.`);
}

for (const file of htmlFiles) {
  const html = readBuiltFile(file);

  if (html === undefined) {
    continue;
  }

  const head = /<head[^>]*>([\s\S]*?)<\/head\s*>/i.exec(html)?.[1] ?? html;
  const body = /<body[^>]*>([\s\S]*?)<\/body\s*>/i.exec(html)?.[1] ?? html;

  const ids = new Set(
    [...html.matchAll(/\sid\s*=\s*"([^"]+)"/g)].map((match) => {
      try {
        return decodeURIComponent(match[1]);
      } catch {
        return match[1];
      }
    }),
  );
  pageIds.set(file, ids);

  const h1Count = (body.match(/<h1(?=[\s>])/gi) ?? []).length;

  if (h1Count !== 1) {
    fail(`${file} has ${h1Count} level-one headings, and every page carries exactly one.`);
  }

  const titles = [...head.matchAll(/<title[^>]*>([\s\S]*?)<\/title\s*>/gi)].map((match) =>
    plainText(match[1]),
  );

  if (titles.length !== 1) {
    fail(`${file} has ${titles.length} document titles, and every page carries exactly one.`);
  } else {
    const seen = pageTitles.get(titles[0]) ?? [];
    seen.push(file);
    pageTitles.set(titles[0], seen);
  }

  const canonicals = [...head.matchAll(/<link\b[^>]*?\brel\s*=\s*"canonical"[^>]*?>/gi)];

  // A noindex page, such as 404.html, is served at every unknown address, so it names no canonical URL.
  const noindex = /<meta\b[^>]*?name\s*=\s*"robots"[^>]*?noindex/i.test(head);

  // The page's clean URL: index.html is the home page, every other page is its path without .html.
  const cleanUrl = file === 'index.html' ? `${siteUrl}/` : `${siteUrl}/${file.replace(/\.html$/, '')}`;

  if (noindex && canonicals.length === 0) {
    // A noindex page with no canonical link is correct.
  } else if (canonicals.length !== 1) {
    fail(`${file} has ${canonicals.length} canonical links, and every page carries exactly one.`);
  } else {
    const href = /href\s*=\s*"([^"]*)"/i.exec(canonicals[0][0])?.[1] ?? '';

    if (href !== siteUrl && !href.startsWith(`${siteUrl}/`)) {
      fail(`${file} names '${href}' as canonical, which is not an absolute site URL.`);
    } else if (!noindex && href !== cleanUrl) {
      fail(`${file} names '${href}' as canonical, while its clean URL is '${cleanUrl}'.`);
    }
  }

  // The shared link of the page is the same clean URL, never a built file name.
  if (!noindex) {
    const ogUrls = [...head.matchAll(/<meta\b[^>]*?\bproperty\s*=\s*"og:url"[^>]*?>/gi)];

    if (ogUrls.length !== 1) {
      fail(`${file} has ${ogUrls.length} og:url tags, and every indexed page carries exactly one.`);
    } else {
      const content = /content\s*=\s*"([^"]*)"/i.exec(ogUrls[0][0])?.[1] ?? '';

      if (content !== cleanUrl) {
        fail(`${file} names '${content}' as og:url, while its clean URL is '${cleanUrl}'.`);
      }
    }
  }

  // No breadcrumb trail names the same URL twice: a folder crumb without a page of its own stays out of the
  // structured data instead of repeating the current page URL.
  for (const script of head.matchAll(
    /<script\b[^>]*?\btype\s*=\s*"application\/ld\+json"[^>]*?>([\s\S]*?)<\/script\s*>/gi,
  )) {
    let data;

    try {
      data = JSON.parse(script[1]);
    } catch {
      continue;
    }

    const nodes = Array.isArray(data?.['@graph']) ? data['@graph'] : [data];

    for (const node of nodes) {
      if (node === null || typeof node !== 'object' || node['@type'] !== 'BreadcrumbList') {
        continue;
      }

      const elements = Array.isArray(node.itemListElement) ? node.itemListElement : [];
      const seen = new Set();

      for (const element of elements) {
        const key =
          typeof element?.item === 'string' ? element.item : JSON.stringify(element?.item ?? null);

        if (seen.has(key)) {
          fail(`${file} lists '${key}' twice in its breadcrumb structured data.`);
        }

        seen.add(key);
      }
    }
  }

  // Attribute values may hold a literal ">" (such as "Handler<T>"), so quoted values are skipped whole.
  const descriptions = [...head.matchAll(/<meta\b(?:[^>"]|"[^"]*")*?\bname\s*=\s*"description"(?:[^>"]|"[^"]*")*>/gi)];

  if (descriptions.length !== 1) {
    fail(`${file} has ${descriptions.length} meta descriptions, and every page carries exactly one.`);
  } else {
    const content = plainText(/content\s*=\s*"([^"]*)"/i.exec(descriptions[0][0])?.[1] ?? '');

    if (content.length === 0) {
      fail(`${file} has an empty meta description.`);
    } else if (content.length > 160) {
      fail(`${file} has a meta description of ${content.length} characters, and the limit is 160.`);
    }
  }
}

for (const [title, files] of [...pageTitles.entries()].sort(([a], [b]) => (a < b ? -1 : 1))) {
  if (files.length > 1) {
    fail(`document title "${title}" is not unique: ${files.join(', ')}.`);
  }
}

// Every internal href resolves to a built file. Same-origin absolute links are checked the same way as
// root-relative ones; anything else with a scheme, such as mailto: and other hosts, is left alone.
for (const file of htmlFiles) {
  const html = readBuiltFile(file);

  if (html === undefined) {
    continue;
  }

  const directory = posix.dirname(file);

  for (const match of html.matchAll(/<([A-Za-z][A-Za-z0-9]*)[^<>]*?\bhref\s*=\s*"([^"]*)"[^<>]*>/g)) {
    const tag = match[1].toLowerCase();
    const href = match[2].trim();

    if (href.length === 0) {
      continue;
    }

    checkedLinks += 1;
    const hashStart = href.indexOf('#');
    const base = hashStart === -1 ? href : href.slice(0, hashStart);
    const fragment =
      hashStart === -1 ? undefined : (() => {
        try {
          return decodeURIComponent(href.slice(hashStart + 1));
        } catch {
          return href.slice(hashStart + 1);
        }
      })();
    const path = base.includes('?') ? base.slice(0, base.indexOf('?')) : base;

    let relative;
    let samePage = false;

    if (path === '') {
      samePage = true;
      relative = file;
    } else if (absoluteHrefPattern.test(path)) {
      if (path === siteUrl || path.startsWith(`${siteUrl}/`)) {
        relative = path.slice(siteUrl.length).replace(/^\//, '');
      } else {
        continue;
      }
    } else if (path.startsWith('/')) {
      relative = path.slice(1);
    } else {
      // A page-relative link resolves against the page's own directory in dist/.
      relative = posix.normalize(posix.join(directory === '.' ? '' : directory, path));

      if (relative.startsWith('..')) {
        fail(`${file} links to '${href}', which escapes the built site.`);
        continue;
      }
    }

    // A rendered link that still ends in .md is a source-file link the renderer failed to rewrite, except for the
    // "View Markdown" links in the docs pages, the Markdown alternate links in the head, and absolute agent links.
    if (/\.md$/i.test(relative)) {
      const close = tag === 'a' ? html.indexOf('</a', match.index + match[0].length) : -1;
      const anchorText =
        close === -1 ? '' : plainText(html.slice(match.index + match[0].length, close)).slice(0, 80);
      const allowed =
        tag === 'link' || /^View(\s+as)?\s+Markdown$/.test(anchorText) || absoluteHrefPattern.test(href);

      if (!allowed) {
        fail(`${file} links to the Markdown source '${href}'.`);
      }
    }

    const target = samePage ? file : resolveBuiltFile(relative, builtFiles);

    if (target === undefined) {
      fail(`${file} links to '${href}', which was not built.`);
      continue;
    }

    // A hash has to name an id on the target page, or the link lands at the top instead of its section.
    if (fragment !== undefined && fragment !== '' && target.endsWith('.html')) {
      if (!pageIds.get(target)?.has(fragment)) {
        fail(`${file} links to '${href}', whose hash names no id on ${target}.`);
      }
    }
  }
}

// The plain-text views are what an agent reads instead of the site, and nothing about them is visible in a browser,
// so a defect in them is invisible until someone fetches one. The index follows the llms.txt shape, every link in it
// is absolute and resolves to a Markdown endpoint that was actually built, every entry carries a note, and the
// complete text holds every page the index lists with no relative link left in it.
const indexPath = 'llms.txt';
const fullPath = 'llms-full.txt';
const index = readBuiltFile(indexPath);
const full = readBuiltFile(fullPath);
const indexedMarkdownUrls = [];

if (index === undefined) {
  fail(`no index was built at /${indexPath}.`);
} else if (full === undefined) {
  fail(`no complete text was built at /${fullPath}.`);
} else {
  const lines = index.split('\n');
  const firstContent = lines.find((line) => line.trim().length > 0);

  // The convention requires an H1 naming the project, then a blockquote summary, then file lists under H2 headings.
  // Anything deeper than H2 would make a list ambiguous about which section it belongs to.
  if (firstContent === undefined || !firstContent.startsWith('# ')) {
    fail(`/${indexPath} does not open with a level-one heading.`);
  }

  const headings = lines.filter((line) => /^#{1,6}\s/.test(line));
  const firstSection = lines.findIndex((line) => line.startsWith('## '));

  if (headings.filter((heading) => heading.startsWith('# ')).length !== 1) {
    fail(`/${indexPath} has ${headings.length} level-one headings, and the convention allows one.`);
  }

  if (firstSection === -1) {
    fail(`/${indexPath} has no level-two section.`);
  }

  if (!lines.slice(0, firstSection === -1 ? undefined : firstSection).some((line) => line.startsWith('> '))) {
    fail(`/${indexPath} has no blockquote summary above its first section.`);
  }

  const deepHeading = headings.find((heading) => /^#{3,6}\s/.test(heading));

  if (deepHeading !== undefined) {
    fail(`/${indexPath} has a heading below level two (${deepHeading.trim()}).`);
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
      fail(`/${indexPath} links '${link.text}' to '${link.url}', which is not an absolute site URL.`);
      continue;
    }

    if (!/\.md(?:#|$)/.test(link.url)) {
      fail(`/${indexPath} links '${link.text}' to '${link.url}', which is not a Markdown endpoint.`);
      continue;
    }

    const markdownPath = link.url.slice(siteUrl.length + 1).split('#')[0];

    if (!builtFiles.has(markdownPath)) {
      fail(`/${indexPath} links '${link.text}' to '${link.url}', which was not built.`);
    }

    indexedMarkdownUrls.push(`${siteUrl}/${markdownPath}`);

    // The note after the link is the page's description. A missing one means a page the description extraction
    // could not read, and an index entry that says nothing about the page it points at.
    if (!/\):\s\S/.test(entry)) {
      fail(`/${indexPath} lists '${link.text}' with no note.`);
    }
  }

  // Every other Markdown link in the index answers the same rule: absolute, and to a built Markdown endpoint.
  for (const link of markdownLinks(withoutCodeFences(index))) {
    if (listedEntries.some((entry) => entry.includes(`](${link.url})`))) {
      continue;
    }

    if (!link.url.startsWith(`${siteUrl}/`) || !/\.md(?:#|$)/.test(link.url)) {
      fail(`/${indexPath} links '${link.text}' to '${link.url}', which is not a site Markdown endpoint.`);
    } else if (!builtFiles.has(link.url.slice(siteUrl.length + 1).split('#')[0])) {
      fail(`/${indexPath} links '${link.text}' to '${link.url}', which was not built.`);
    }
  }

  const missing = [...new Set(indexedMarkdownUrls)].filter((url) => !full.includes(url));

  if (indexedMarkdownUrls.length === 0) {
    fail(`/${indexPath} lists no pages, so /${fullPath} has nothing to reproduce.`);
  } else if (missing.length > 0) {
    fail(
      `/${fullPath} does not reproduce every indexed page: ${missing.slice(0, 5).join(', ')}${
        missing.length > 5 ? ` and ${missing.length - 5} more` : ''
      }.`,
    );
  }

  // A relative path resolves against nothing once the pages are concatenated, so one surviving here is a link that
  // silently leads nowhere.
  const relative = markdownLinks(withoutCodeFences(full))
    .map((link) => link.url)
    .filter((url) => !/^(?:[a-z][a-z0-9+.-]*:|\/|#)/i.test(url));

  if (relative.length > 0) {
    fail(`/${fullPath} has ${relative.length} unresolved relative links, such as '${relative[0]}'.`);
  }
}

// The sitemap lists every indexable page and nothing that is not built: the home page, the privacy page, and every
// docs page. Release roots, raw Markdown copies, agent indexes, and the 404 page stay out of it.
const sitemap = readBuiltFile('sitemap.xml');

if (sitemap === undefined) {
  fail('no sitemap was built at /sitemap.xml.');
} else {
  const locations = [...sitemap.matchAll(/<loc>\s*([^<]+?)\s*<\/loc>/g)].map((match) => match[1].trim());
  const listedFiles = new Set();

  for (const location of locations) {
    if (location !== siteUrl && !location.startsWith(`${siteUrl}/`)) {
      fail(`/sitemap.xml lists '${location}', which is not an absolute site URL.`);
      continue;
    }

    const pathname = location.slice(siteUrl.length).replace(/^\//, '').split(/[?#]/)[0];

    if (/\.md$/i.test(pathname)) {
      fail(`/sitemap.xml lists '${location}', and Markdown endpoints stay out of the sitemap.`);
      continue;
    }

    const target = resolveBuiltFile(pathname, builtFiles);

    if (target === undefined) {
      fail(`/sitemap.xml lists '${location}', which was not built.`);
    } else {
      listedFiles.add(target);
    }
  }

  const indexablePages = [...builtFiles].filter(
    (file) =>
      file === 'index.html' ||
      file === 'privacy.html' ||
      file === 'docs.html' ||
      (file.startsWith('docs/') && file.endsWith('.html')),
  );

  for (const page of indexablePages) {
    if (!listedFiles.has(page)) {
      fail(`/sitemap.xml does not list /${page.replace(/\.html$/, '').replace(/^index$/, '') || ''} (${page}).`);
    }
  }

  if (listedFiles.has('404.html')) {
    fail('/sitemap.xml lists the 404 page, which stays out of the sitemap.');
  }
}

// Every redirect target resolves to a built page, so an old URL lands on content instead of the 404 page, and every
// redirect the previous site served still has a line.
const redirectsFile = readBuiltFile('_redirects');

if (redirectsFile === undefined) {
  fail('no redirects were built at /_redirects.');
} else {
  // The sources of the previous site's redirects, in Cloudflare _redirects form with `:name` and `:name*`
  // parameters written as `*`. A source missing here is an old URL that stops working.
  const expectedSources = [
    '/docs/llms.txt',
    '/docs/llms-full.txt',
    '/v7/docs',
    '/v7/docs/*',
    '/docs/v7/*',
    '/v7',
    '/docs/concepts/domain-events-and-unit-of-work',
    '/docs/concepts/recurring-commands',
    '/docs/reference/feature-index-v7',
    '/docs/roadmap/marten-outbox-writer',
    '/docs/reference/feature-index-v6',
    '/docs/reference/semantic-validation',
    '/docs/migration/from-mediatr',
    '/docs/integrations/inbox-amqp',
    '/docs/catalog/hosting/module-registry',
    '/docs/catalog/saga/processor-envelope-hooks',
  ];
  const actualSources = [];

  for (const line of redirectsFile.split('\n')) {
    const trimmed = line.trim();

    if (trimmed === '' || trimmed.startsWith('#')) {
      continue;
    }

    const [source, target] = trimmed.split(/\s+/);

    if (source === undefined || target === undefined) {
      fail(`/_redirects has a malformed line: '${trimmed}'.`);
      continue;
    }

    actualSources.push(source.replace(/:[A-Za-z0-9_-]+\*?/g, '*'));

    let pathname;

    if (target.startsWith('/')) {
      pathname = target;
    } else if (target === siteUrl || target.startsWith(`${siteUrl}/`)) {
      pathname = target.slice(siteUrl.length);
    } else if (absoluteHrefPattern.test(target)) {
      continue;
    } else {
      fail(`/_redirects sends '${source}' to '${target}', which is neither a site path nor an absolute URL.`);
      continue;
    }

    // A splat target matches many pages, so only its static prefix is checked: it has to be a built file or a
    // folder holding built files.
    if (pathname.includes(':') || pathname.includes('*')) {
      const prefix = pathname
        .slice(0, Math.min(...[pathname.indexOf(':'), pathname.indexOf('*')].filter((at) => at !== -1)))
        .replace(/\/$/, '')
        .replace(/^\//, '');
      const prefixFile = resolveBuiltFile(prefix, builtFiles);
      const prefixHoldsFiles = [...builtFiles].some((file) => file.startsWith(`${prefix}/`));

      if (prefixFile === undefined && !prefixHoldsFiles) {
        fail(`/_redirects sends '${source}' to '${target}', whose prefix was not built.`);
      }

      continue;
    }

    if (resolveBuiltFile(pathname.replace(/^\//, '').split(/[?#]/)[0], builtFiles) === undefined) {
      fail(`/_redirects sends '${source}' to '${target}', which was not built.`);
    }
  }

  for (const expected of expectedSources) {
    if (!actualSources.includes(expected)) {
      fail(`/_redirects has no line for '${expected}', which the previous site redirected.`);
    }
  }
}

// The documentation naming model, checked against the Markdown sources: a page has one name, its H1, and a folder
// label is that same name on the folder's index page, so the sidebar, breadcrumb, tab title, search result, and
// plain-text entry for a page can all show one text. Each folder label matches its index page H1, and page names are
// unique within a top-level section.
if (!existsSync(docsRoot)) {
  fail(`documentation content was not found at ${docsRoot}.`);
} else {
  /** The text of the first level-one heading. */
  const extractTitle = (content) => {
    const text = content.charCodeAt(0) === 0xfeff ? content.slice(1) : content;

    return /^#\s+(.+?)\s*$/m.exec(text)?.[1];
  };

  /** Every directory under a root, including the root itself. */
  const walkDirectories = (root) => {
    const directories = [root];

    for (const entry of readdirSync(root, { withFileTypes: true })) {
      if (entry.isDirectory()) {
        directories.push(...walkDirectories(join(root, entry.name)));
      }
    }

    return directories;
  };

  /** Every Markdown source under a root, as paths relative to the docs root with forward slashes. */
  const walkPages = (root) => {
    const pages = [];

    for (const directory of walkDirectories(root)) {
      for (const entry of readdirSync(directory, { withFileTypes: true })) {
        if (entry.isFile() && entry.name.endsWith('.md')) {
          pages.push(join(directory, entry.name).slice(root.length + 1).replaceAll('\\', '/'));
        }
      }
    }

    return pages.sort();
  };

  /** The top-level section a page belongs to: its first path segment, or the root for the index page. */
  const sectionOf = (page) => {
    const segment = page.split('/')[0];

    return segment === 'index.md' ? '(root)' : segment;
  };

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

    const indexSource = ['README.md', 'index.md']
      .map((name) => join(directory, name))
      .find((candidate) => existsSync(candidate));

    if (indexSource === undefined) {
      continue;
    }

    const page = indexSource.slice(docsRoot.length + 1).replaceAll('\\', '/');
    const title = extractTitle(readFileSync(indexSource, 'utf8'));

    if (title === undefined) {
      fail(`folder index "${page}" has no level-one heading to match its "${label}" label.`);
    } else if (title !== label) {
      fail(
        `folder "${directory.slice(docsRoot.length + 1).replaceAll('\\', '/') || '(root)'}" is labelled "${label}" but its index page "${page}" is named "${title}".`,
      );
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
        fail(`page name "${title}" is not unique in section "${section}": ${pages.join(', ')}.`);
      }
    }
  }
}

if (failures.length > 0) {
  console.error(`The built-site check failed with ${failures.length} problem${failures.length === 1 ? '' : 's'}:`);

  for (const failure of failures) {
    console.error(`  - ${failure}`);
  }

  process.exitCode = 1;
} else {
  console.log(
    `Checked ${htmlFiles.length} built pages, ${checkedLinks} links, ${indexedMarkdownUrls.length} index entries, the sitemap, and the redirects.`,
  );
  console.log('Plain-text documentation views validated.');
  console.log('Documentation titles validated.');
}
