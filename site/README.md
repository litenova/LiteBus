# litebus.io

The `litebus.io` site publishes the LiteBus documentation as HTML for readers and plain text for agents, built from the Markdown under `site/content/docs/`.

## Audience

You maintain the `litebus.io` site in `site/`. You change its design, its agent endpoints, its routes, or its deployment. After reading this page, you can run the site, find the code behind each behavior, and make the common changes without breaking the build.

## What It Is

This is the static Astro 7 site that publishes the LiteBus documentation at `https://litebus.io`. Everything it serves comes from the Markdown under `content/docs/` through one loader, `src/lib/docs.ts`. Human readers get HTML pages. Agents get `/llms.txt`, a raw `.md` copy of every page, and `/llms-full.txt` with every page in one document. The styling is hand-written CSS with no framework. Mermaid diagrams render to inline SVG at build time.

The project file is `site/package.json`, naming the package `litebus-docs` with its dependencies and scripts. The package manager is pnpm 11.1.3, pinned by the `packageManager` field and the `pnpm-lock.yaml` lockfile, with Node 24 or later. The TypeScript config `site/tsconfig.json` extends the Astro strict config and leaves `dist/` out. The ignore file `site/.gitignore` leaves `node_modules/`, `dist/`, `.astro/`, `.wrangler/`, and the synced `content/docs/changelog.md` out of git. The pnpm workspace file `site/pnpm-workspace.yaml` allows the native builds the dependencies need.

## Quick Start

Run every command in this section from `site/`, or with `pnpm --dir site` from the repository root.

Install the dependencies once:

```bash
pnpm install
```

Start the dev server:

```bash
pnpm dev
```

Build the static output with its search index:

```bash
pnpm build
```

Preview what the build produced:

```bash
pnpm preview
```

Behind these scripts, `dev` runs `astro dev --port 3100`, `build` runs `astro build && pagefind --site dist`, and `preview` runs `astro preview --port 3100`. Open `http://localhost:3100`.

Run the site tests, which exercise its pure helpers:

```bash
pnpm test
```

Run the TypeScript check and the built-site gate:

```bash
pnpm typecheck
pnpm check
```

Before `dev` and `build`, the `sync:changelog` step copies the repository `Changelog.md` to `content/docs/changelog.md`, so the changelog page goes through the same navigation, search, tab title, and plain-text views as every other page. That copy is ignored by git and must not be edited.

## Layout

Config files live at the top of `site/`. Source code lives under `src/lib/`, `src/components/`, `src/layouts/`, `src/pages/`, and `src/styles/`, static files live in `public/`, and build scripts live in `scripts/`.

| Path | What it owns |
|:---|:---|
| `astro.config.mjs` | Site origin `https://litebus.io`, static output, no trailing slashes, and file URLs (`/docs/x` renders `docs/x.html`). |
| `package.json` | Scripts and dependencies named above. |
| `tsconfig.json` | The Astro strict extension and the `dist/` exclusion. |
| `pnpm-workspace.yaml` | The allowed native builds. |
| `wrangler.jsonc` | The `litebus-site` Workers deployment: build command, `dist/` static assets, and the `litebus.io` custom domain. |
| `wrangler.www.jsonc` | The `litebus-www` redirect Worker deployment for `www.litebus.io`. |
| `workers/www-redirect.js` | The redirect itself: every `www.litebus.io` request answers 301 to the same path and query on `litebus.io`. |
| `src/lib/site.ts` | Site facts every page reads: origin, name, tagline, title, description, share image, `latestRelease`, docs base path and content dir, repository URLs, company and author, and keywords. |
| `src/lib/docs.ts` | The one loader every page and endpoint reads. It walks `content/docs`, derives titles and descriptions, builds the navigation tree, and resolves relative links. |
| `src/lib/docs.test.ts` | Loader tests: slug and URL mapping, title and description extraction, nav ordering with separators, and link resolution. |
| `src/lib/render-markdown.ts` | The Markdown to HTML pipeline with Mermaid diagrams, Shiki highlighting, heading ids, the table of contents, and link rewriting, plus `highlight()` for the home page snippets. |
| `src/lib/render-markdown.test.ts` | Rendering tests: heading ids and toc depth, table wrap, and the code-block figure. |
| `src/lib/code-blocks.ts` | The code block figure, header strip, language label, and copy button. |
| `src/lib/code-blocks.test.ts` | Code block tests. |
| `src/lib/seo.ts` | Metadata helpers: canonical URLs, page titles, descriptions, the share image, and the JSON-LD builders. |
| `src/lib/seo.test.ts` | Metadata tests for page titles, descriptions, and canonical URLs. |
| `src/lib/llms.ts` | The plain-text builders for the agent index, the complete text, and the per-page Markdown. |
| `src/lib/llms.test.ts` | Plain-text builder tests. |
| `src/lib/icons.ts` | The fixed line icon set, drawn in the style of Lucide on a 24px grid. |
| `src/layouts/Base.astro` | The shared layout with the head, the theme script, the header, the footer, and the copy behavior. |
| `src/layouts/Docs.astro` | The three-column docs frame: sidebar, article with breadcrumbs and meta row, table of contents, and pager. |
| `src/components/DocsSidebar.astro` | The left navigation tree, opening as a drawer on phones. |
| `src/components/PageToc.astro` | The right `On this page` list. |
| `src/components/TocList.astro` | The shared list the table of contents renders. |
| `src/components/Breadcrumbs.astro` | The breadcrumb trail above the article. |
| `src/components/Pager.astro` | The Previous and Next cards below the article. |
| `src/components/Search.astro` | The Pagefind search trigger and dialog. |
| `src/components/ThemeSwitcher.astro` | The theme override button. |
| `src/components/Icon.astro` | One inline icon from `src/lib/icons.ts`. |
| `src/components/Mark.astro` | The LiteBus logo, drawn inline from `public/logo.svg`. |
| `src/components/HomeStepper.astro` | The three-step install walkthrough on the home page, present only while that interaction is kept. |
| `src/pages/index.astro` | The home page route. |
| `src/pages/docs/[...slug].astro` | One HTML page per documentation page, including `/docs` itself. |
| `src/pages/docs.md.ts` | The raw Markdown of the docs index at `/docs.md`. |
| `src/pages/docs/[...slug].md.ts` | The raw Markdown of one page at `/docs/<path>.md`. |
| `src/pages/llms.txt.ts` | The agent index at `/llms.txt`. |
| `src/pages/llms-full.txt.ts` | The complete text of every page at `/llms-full.txt`. |
| `src/pages/sitemap.xml.ts` | The sitemap: home, privacy, and every docs page. |
| `src/pages/404.astro` | The not-found page. |
| `src/pages/privacy.astro` | The privacy statement. |
| `src/styles/site.css` | The palette, the semantic tokens, the frame, the header, the footer, and the shared primitives. |
| `src/styles/docs.css` | The three-column docs layout, the sidebar tree and its phone drawer, the breadcrumb, the page meta row, the pager, and the `On this page` list. |
| `src/styles/prose.css` | The rendered page body: headings, tables, and code. |
| `src/styles/code.css` | The highlighted code blocks and the Shiki theme selection. |
| `src/styles/home.css` | The home page: hero, facts, sections, stepper, and call-to-action rows. |
| `public/robots.txt` | Allows everything, names the sitemap, and points agents at `/llms.txt`. |
| `public/site.webmanifest` | The application manifest: name, description, start URL, theme colors, and icons. |
| `public/_headers` | Security headers, cache rules, Markdown content types, and the `llms.txt` alternate link. |
| `public/_redirects` | Every redirect the site keeps working: the agent index aliases, the legacy version prefixes, and the moved pages. |
| `public/favicon.svg`, `public/favicon-32.png`, `public/apple-touch-icon.png`, `public/icon-192.png`, `public/icon-512.png` | The browser icon, the touch and app icons, derived from `icon.svg` and `icon.png`. |
| `public/logo.svg`, `public/icon.svg`, `public/icon.png` | The LiteBus logo and mark. |
| `public/litenova-mark.svg` | The LiteNova Solutions mark for the footer. |
| `public/og.png` | The share image. |
| `public/fonts/` | The Geist Latin variable fonts with `OFL.txt` under the SIL Open Font License. |
| `public/.well-known/security.txt` | The security contact (RFC 9116). |
| `scripts/check-site.mjs` | The built-site gate, run by `pnpm check` against `dist/`. |
| `scripts/sync-changelog.mjs` | Publishes the repository `Changelog.md` as `content/docs/changelog.md` before `dev` and `build`. |

### How It Works

#### The Loader

No module except `src/lib/docs.ts` reads the documentation. It locates the content folder from the working directory (the build runs in `site/`), falling back to walking up until it finds `content/docs/meta.json`. It reads every `.md` file under `content/docs` once, including the synced `changelog.md`. A `DocPage` carries its repository-relative `file`, its `slug` (lower-case path segments without `.md`; a file named `README.md` or `index.md` is its folder's index), its `url` (`/docs` or `/docs/<path>`), its `mdUrl` (the URL plus `.md`), its `title`, its `description`, its raw `markdown`, its `section` (the top-level folder label, or `Documentation` for the index), and its `lastModified` (the ISO date of the last git commit touching the file, or the build date).

The Markdown sources carry no frontmatter. Each page title is the first level-one heading in its source. Each description comes from the intro paragraph, an explicit `Summary` metadata field, or the `## Summary` section, whichever the page uses, falling back to the site description. The description feeds the page's meta and Open Graph tags, the search index, and the note beside each entry in `llms.txt`.

The navigation tree follows the `meta.json` semantics the site has always used: the `pages` array lists child files or folders in order, `---Label---` is a separator, and a child missing from `pages` is appended after the listed ones, sorted by title. A folder's label is its `meta.json` `title`, else its index page title. The `pages` array holds every page in reading order (the nav tree order, depth first). The same module owns the pager neighbors (`pageNeighbors()`), the breadcrumb trail (`breadcrumbs()`, with `Docs` first and the current page last without a URL), and the reading time (`readingTime()`, minutes at 220 words a minute, minimum one).

Pages link to each other by relative source path (`../getting-started/README.md`) so the Markdown stays readable in the repository and on GitHub. `resolveDocHref()` turns those into site URLs at render time, keeping `#hash` fragments and sending links to files the site does not serve to the repository at the documented source ref. `absoluteMarkdownHref()` resolves the same links to absolute `.md` URLs for the agent texts.

#### Rendering

One documentation page becomes served HTML in `src/lib/render-markdown.ts`. The module creates its Markdown processor from `@astrojs/markdown-remark` with the `remarkOndine` plugin for Mermaid fences, Shiki highlighting with the `github-light` and `github-dark` themes emitted as CSS variables, and GitHub-style heading ids. HTML post-processing then runs in this order:

1. Heading ids from the GitHub slug, with the `H1` removed (the template renders the title).
2. A table of contents of `H2` entries with `H3` entries nested inside.
3. Relative links between docs pages rewritten to site URLs through `resolveDocHref()`.
4. Mermaid blocks swapped for inline SVG diagrams.
5. Tables wrapped in `table-wrap`.
6. Code blocks wrapped in the `code-block` figure.

`src/lib/code-blocks.ts` wraps each highlighted block in a `code-block` figure with a header strip that shows the language name and a copy button. External links get `rel="noopener"`.

#### Plain-Text Views

The site serves the documentation as plain text as well as HTML, following the llms.txt convention (<https://llmstxt.org>). This is what an agent or a language model reads instead of scraping rendered pages. It is built from the same sources and the same page tree the navigation uses, so it cannot describe a different set of pages.

| URL | Holds |
|-----|-------|
| `/llms.txt` | Index: a summary of the library, orientation prose, a curated `Start Here` list, then every page grouped under the `meta.json` separators, with the roadmap and contributing guides under `Optional`. |
| `/llms-full.txt` | Every page, complete, in one document. |
| `/docs/<path>.md` | One page's Markdown. Appending `.md` to any documentation URL returns its source. |

`src/lib/llms.ts` builds all three. The summary, the orientation prose, and the `Start Here` list are hand-written in that file, because a client arriving with no context needs the shape of the library before it needs a file listing. Everything below `Start Here` comes from the page tree, so a page added to `meta.json` appears without anyone remembering to add it. A curated slug that does not resolve throws at build time, which keeps the list from quietly rotting as pages move.

Links in these files point at `.md` endpoints rather than at pages, so following one returns Markdown rather than an application shell. Relative links are rewritten to absolute URLs in all three files, because a relative path resolves to nothing once a page is served on its own or concatenated with every other page. Endpoints answer `text/plain; charset=utf-8` for `.txt` and `text/markdown; charset=utf-8` for `.md`.

A client can also ask for a page's Markdown without using the `.md` URL: requesting the page URL with an `Accept` header that prefers `text/markdown` returns the same Markdown with `Content-Type: text/markdown; charset=utf-8`, `Vary: Accept`, `X-Robots-Tag: noindex`, and a `Link` header naming the HTML page canonical. The `.md` URLs stay the stable, documented form; the header form exists for agents that request page URLs directly. Browsers, whose headers rank HTML first, always receive HTML. The negotiation script is described under Deployment.

The built-site gate in `scripts/check-site.mjs` asserts the shape: the index opens with one H1 and a blockquote summary, every link in it is absolute and reaches a Markdown endpoint that was built, every entry carries a note, and the complete text holds every page the index lists, with no relative link left in it.

#### Routes

| Route file | URL | What it writes |
|:---|:---|:---|
| `src/pages/index.astro` | `/` | The landing page: the hero with the version chip, the headline, the install command, and the Target, Language and License facts; the three-step walkthrough; the feature groups; and the MediatR comparison. |
| `src/pages/docs/[...slug].astro` | `/docs`, `/docs/<path>` | One HTML page per documentation page in the docs frame: the sidebar, the breadcrumb, the title, the meta row with reading time, last-updated date, `View Markdown` and `Edit on GitHub` links, the rendered body, and the pager. |
| `src/pages/docs.md.ts` | `/docs.md` | The raw Markdown of the docs index. |
| `src/pages/docs/[...slug].md.ts` | `/docs/<path>.md` | The raw Markdown of one page with links rewritten to absolute URLs. |
| `src/pages/llms.txt.ts` | `/llms.txt` | The agent index. |
| `src/pages/llms-full.txt.ts` | `/llms-full.txt` | The complete text of every page. |
| `src/pages/sitemap.xml.ts` | `/sitemap.xml` | The home page, the privacy page, and every docs page with `lastmod` dates. No `.md` URLs. |
| `src/pages/privacy.astro` | `/privacy` | The privacy statement. |
| `src/pages/404.astro` | Any unknown path | The not-found page with a link to `/docs`, search, and the top-level docs sections. |

`astro.config.mjs` sets `site` to `https://litebus.io`, `output` to `static`, `trailingSlash` to `never`, and `build.format` to `file`. URLs never end with `/` except the home page. Slugs are the lower-case path segments of the file path under `content/docs/` without `.md`.

#### Search

Indexing happens in the build through `pagefind --site dist`, the second half of the `build` script. `Search.astro` loads `/pagefind/pagefind.js` lazily on first use and queries through the Pagefind JS API without the default UI. The dialog answers `/` or Ctrl/Cmd+K, arrows, `Enter`, and `Esc`. Against a dev server with no index, it reads that search is available on the built site.

Pagefind indexes the text under `data-pagefind-body` on the article body div, titled by `data-pagefind-meta="title"` on its `H1`. Interface text outside the body stays out of the index.

#### Design System

Palette and semantic tokens live in `src/styles/site.css`, and every component takes its colors from tokens alone. Each token names a role instead of a value: `--bg`, `--fg`, `--muted`, `--accent`, and the rest of the set. `site.css` is the one file allowed to hold raw color values.

Dark mode has two equivalent paths: the `prefers-color-scheme` media query applying to `:root` without a theme override, and the explicit `:root[data-theme="dark"]` override setting the same values. `ThemeSwitcher.astro` overrides the system scheme or returns to it, persisting the pick in `localStorage` under `litebus-theme`. `Base.astro` consults that store before first paint, so the theme applies without a flash, and keeps `<meta name="color-scheme">` in sync.

The page frame is a centered column: `.frame` limits width, stretches at least to viewport height, and contains the header and footer. The header carries the logo, the Docs link, the version chip linking to the changelog, search, the GitHub and NuGet links, and the theme switcher. The footer carries the LiteNova Solutions link, the MIT license note, the Privacy link, the `llms.txt` and `llms-full.txt` agent links, and the GitHub link. The typefaces are Geist and Geist Mono, served from `public/fonts/` as Latin variable fonts under the SIL Open Font License, with the body font preloaded.

The fixed line icon set in `src/lib/icons.ts` follows the Lucide style on a 24px grid. `src/components/Icon.astro` renders one entry inline. An icon stays decorative unless given a `label`.

#### Components

| Component | What it owns |
|:---|:---|
| `src/layouts/Base.astro` | The shared layout: the head with the title, the description trimmed to 160 characters, the canonical URL, robots, theme colors, Open Graph and Twitter cards with the share image, icons, the manifest, the Markdown and plain-text alternate links, the font preload, and one JSON-LD graph; the theme script before first paint; the framed column with the header and the footer; and the copy-button click behavior. |
| `src/layouts/Docs.astro` | The docs frame: `DocsSidebar.astro`, the article, and `PageToc.astro`. |
| `src/components/DocsSidebar.astro` | The left navigation: the full documentation tree in reading order, with separators as group labels, folders as expandable groups opened when they contain the current page, and `aria-current="page"` on the current link. On phones the same markup opens as a drawer. |
| `src/components/Breadcrumbs.astro` | The breadcrumb trail as an ordered list, with the current page as text. |
| `src/components/PageToc.astro` | The right `On this page` list of `H2` headings with their `H3` entries nested beneath, with the section in view marked. |
| `src/components/Pager.astro` | The Previous and Next cards. |
| `src/components/Search.astro` | The search trigger and dialog. |
| `src/components/ThemeSwitcher.astro` | The theme override button. |
| `src/components/Mark.astro` | The LiteBus mark drawn inline. |

### Common Changes

#### Add a Docs Page

1. Create the Markdown file under `content/docs/` with a lower-case name and one `H1`.
2. List the page in its folder `meta.json` at the position you want in reading order. Sidebar, pager, plain-text views, and sitemap follow the `meta.json` lists, and a page missing from every list sorts after them by title.
3. Rebuild the site. Since `docs/[...slug].astro` derives its paths from `pages`, the new page needs no route file of its own.
4. Confirm the sidebar places it within its folder order, with the folder index page serving as the folder entry, and confirm the pager reaches it.

#### Add a Site Route

1. Create the file under `src/pages/`: `src/pages/help.astro` for HTML or `src/pages/help.txt.ts` for text.
2. Read the documentation exclusively through `src/lib/docs.ts`, and format agent text exclusively through `src/lib/llms.ts`.
3. Declare the content type on API routes: `text/plain; charset=utf-8` for an index, `text/markdown; charset=utf-8` for Markdown.
4. Rebuild, then open the new URL.

#### Add an Icon

1. Append the inner SVG markup to `src/lib/icons.ts`, staying on the 24px grid in the 2px stroke style.
2. Render it with `<Icon name="your-name" />`, adding a `label` only when the icon must carry meaning on its own.
3. Rebuild, then inspect the icon under both color schemes.

#### Change a Color

1. Add or edit the ramp and its semantic token in `src/styles/site.css`, supplying both the light value and the dark override, and write the measured contrast ratio beside the token.
2. Use the token as `var(--your-token)` in the component stylesheet.
3. Leave raw color values out of component stylesheets entirely. `site.css` is the one file allowed to hold them.
4. Verify the result in light mode, in dark mode, and under the system theme on both.

### Deployment

Cloudflare Workers hosts the site as the `litebus-site` Worker, which serves `dist/` as static assets with a small negotiation script in front of the documentation paths. `site/wrangler.jsonc` names the Worker, serves `dist/` with trailing-slash HTML handling and `404.html` for unknown paths, exposes the assets to the script through the `ASSETS` binding, and binds the custom domain `litebus.io`. A companion Worker, `litebus-www` (configured by `wrangler.www.jsonc` with `workers/www-redirect.js`), answers `www.litebus.io` by redirecting with a 301 to the identical path and query on `litebus.io`.

The negotiation script is `site/workers/site.js`. For a `GET` or `HEAD` request to a documentation URL whose `Accept` header prefers `text/markdown` over `text/html`, it answers with that page's `.md` asset as `text/markdown; charset=utf-8`, with `Vary: Accept`, `X-Robots-Tag: noindex`, and a `Link` header naming the HTML page canonical. Every other request is served from static assets unchanged, except that HTML responses under `/docs` gain `Vary: Accept` so caches keep the two variants apart. `run_worker_first` in `wrangler.jsonc` limits the script to `/docs` and `/docs/*`, because only documentation pages have HTML and Markdown variants; every other path is served directly from static assets without running the script. The script has no dependencies: its pure helpers (`prefersMarkdown`, `markdownPathFor`) are plain JavaScript covered by `site/workers/site.test.mjs` (`node --test site/workers/site.test.mjs`).

To check the negotiation against the deployed site, request a docs page with a Markdown `Accept` header and confirm the content type, then request it without the header and confirm the HTML page:

```bash
curl -i -H "Accept: text/markdown" https://litebus.io/docs/concepts/commands
curl -i https://litebus.io/docs/concepts/commands
```

Workers Builds ships the `litebus-site` Worker on each push to `main` and builds a Preview for every other branch. Connect the `litenova/LiteBus` repository in the Cloudflare dashboard with root directory `site`; the build command in `wrangler.jsonc` installs dependencies with pnpm and builds the site into `dist/` before uploading. Each Preview appears on a subdomain of `litebus.io` enabled by the `previews_enabled` route. Because every page declares its `litebus.io` address canonical, no Preview gets indexed as a duplicate site.

For a manual deploy, run the deploy script from `site/`, which builds first through the config above:

```bash
pnpm build
pnpm run deploy
pnpm exec wrangler deploy --config wrangler.www.jsonc   # only when the redirect Worker changes
```

No account id is committed in either config. Workers Builds supplies the account from the dashboard connection, and a manual deploy uses the account of `wrangler login`.

The build emits `dist/_redirects` from `public/_redirects` (sending the agent index aliases, the legacy `/v7/` prefixes, and the moved pages to their current URLs) and copies `public/_headers`. Those headers set the security policy (`Content-Security-Policy`, `Strict-Transport-Security`, `X-Content-Type-Options`, `Referrer-Policy`, and the rest of the `/*` block), long-lived caching for `/_astro/*` and `/fonts/*` with a short cache for `/pagefind/*`, `Content-Type: text/markdown; charset=utf-8` with `X-Robots-Tag: noindex` on the raw Markdown pages and the complete text so that search results point at the HTML page, and a `Link` alternate to `/llms.txt` on every page. The zone enforces Always Use HTTPS, SSL Full (strict), and TLS 1.2 or later.

DNS lives on the `litebus.io` zone in Cloudflare. Add `litebus.io` as a custom domain on the `litebus-site` Worker and `www.litebus.io` as a custom domain on the `litebus-www` Worker; the dashboard shows the records to create, with the apex flattened to the Worker and `www` proxied to the redirect Worker.

#### Moving Off Vercel

The site previously deployed to Vercel from `site/` with `vercel.json`. That file is gone; the steps below retire the Vercel side once the Workers deployment above serves the domain. Do them in order, and confirm each one before the next.

1. Deploy `litebus-site` to Cloudflare and open the Preview URL it reports. Walk the home page, a docs page, a `.md` URL, `/llms.txt`, and one legacy `/v7/` URL, and confirm each answers.
2. Add `litebus.io` as the Worker custom domain and confirm the dashboard reports it active with a valid certificate.
3. Move the DNS records for the apex and `www` to the Cloudflare records from the previous section. Confirm `https://litebus.io` and `https://www.litebus.io` answer from Cloudflare (the latter with a 301 to the apex) from outside your network.
4. Leave the Vercel deployment untouched for one week so cached DNS keeps answering, then remove the `litebus.io` and `www.litebus.io` domains from the Vercel project.
5. Delete the Vercel project once no traffic reaches it.

### Search Engines

Metadata comes from `src/lib/seo.ts`. The `pageTitle` helper joins page title, section, and `LiteBus`, shedding the section past 60 characters so the page title itself is never cut. `absoluteUrl` forms a canonical link on `https://litebus.io` from the page path, `metaDescription` trims a description to 160 characters at a sentence or word boundary, and `techArticle`, `breadcrumbList`, `website`, `organization`, and `softwareSourceCode` assemble the schema.org objects.

Every page declares `<html lang="en">`, carries one `H1` with a unique title and description, and exposes Open Graph tags with the `public/og.png` share image (with width, height, and alt text) plus `summary_large_image` Twitter cards. Each page serializes the Organization together with its own objects into a single JSON-LD graph. Docs pages emit a TechArticle (headline, description, URL, `dateModified` from the page's last git commit, `isPartOf` the WebSite, and the `text/markdown` encoding at the page's `.md` URL) and a BreadcrumbList mirroring the visible breadcrumb. The landing page emits the WebSite and the SoftwareSourceCode, which names the LiteBus library with its repository, C# language, MIT license, .NET platform, and release version.

Diagrams render as inlined SVG with a title and description for assistive technology, while icons stay `aria-hidden` unless labeled. Internal links keep their trailing-slash-free form, the Geist body font preloads with `font-display: swap`, and the head holds no script except the theme read and the JSON-LD. The landing page defines LiteBus in its headline and links to the getting-started guide, the migration comparison, the GitHub repository, and the NuGet packages.

`src/pages/sitemap.xml.ts` enumerates each indexable HTML page (the landing page, the privacy page, and every docs page) as an absolute URL with a `lastmod` date. It leaves out everything that redirects or carries `noindex`: the raw Markdown copies, the `llms.txt` indexes, and the 404 page. `public/robots.txt` permits everything and names the sitemap, while the 404 page itself is `noindex`.

### Testing

Pure helpers are tested with `node --test`. `src/lib/docs.test.ts` pins the slug mapping, title and description extraction, nav ordering, and link resolution, while `render-markdown.test.ts`, `code-blocks.test.ts`, `seo.test.ts`, and `llms.test.ts` cover rendering, code blocks, metadata, and the plain-text builders. Broken pages, broken links, and a broken search index fail the build: `pnpm check` runs `scripts/check-site.mjs` against `dist/` and asserts every internal link resolves, every page carries one h1, title, canonical link, and short meta description with unique titles, the plain-text views hold every page with absolute links, the sitemap lists every page, and every redirect target resolves. A browser pass over the built output verifies layout and theme in both light and dark mode. CI runs the site job in `.github/workflows/ci.yml` after `pnpm install --frozen-lockfile` in `site/`, using the Node and pnpm versions named by the workflow and `package.json`.

## Limits

The site renders only the branch it builds from. There are no versioned documentation snapshots: the pages always describe the source tree they ship with. `latestRelease` in `src/lib/site.ts` is the release label the site shows beside the navigation. It is updated in the commit that cuts a release.

The site runs no server code of its own, migrates no data, and validates no consumer. Search exists solely on built output, since Pagefind writes its index during `build`. JavaScript only enhances the page: theme choice, copy buttons, search, the table of contents marker, and the docs drawer. Without it, every page remains complete. Agent endpoints serve the checked-in documentation as plain text. They compute nothing else.
