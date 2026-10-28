# LiteBus documentation site

This is the Fumadocs site for LiteBus. It serves one documentation set, `content/docs`, at `/docs`.

The Markdown sources carry no frontmatter. Each page title is derived from the first level-one heading in its source, and each description from its intro paragraph, its `Summary` metadata field, or its `## Summary` section, whichever the page uses. See `lib/doc-summary.ts`. The description feeds the page's meta and Open Graph tags, the search index, and the note beside each entry in `llms.txt`. Files named `README.md` are exposed as their directory index, so `getting-started/README.md` is served at `/docs/getting-started`.

Relative Markdown links are resolved through the Fumadocs source loader. The production build rejects rendered local links that still contain a `.md` source-file suffix.

The search route compiles Markdown at runtime, so its narrow Next.js output-file trace includes the content directory for serverless deployments. The build verifies that every Markdown source file is present in that trace.

## One Documentation Set

The site serves `content/docs` from the branch it is built from. There are no versioned snapshots: the pages always describe the source tree they ship with.

`latestRelease` in `lib/site.ts` is the release label the site shows beside the navigation. It is updated in the commit that cuts a release.

Old `/v7/` documentation links redirect to the same path without the prefix, so existing links keep working.

## Plain-Text Views

The site serves the documentation as plain text as well as HTML, following the llms.txt convention (<https://llmstxt.org>). This is what an agent or a language model reads instead of scraping rendered pages. It is built from the same sources and the same page tree the navigation uses, so it cannot describe a different set of pages.

| URL | Holds |
|-----|-------|
| `/llms.txt` | Index: a summary of the library, orientation prose, a curated `Start Here` list, then every page grouped under the `meta.json` separators. |
| `/llms-full.txt` | Every page, complete, in one document. |
| `/docs/<path>.md` | One page's Markdown. Appending `.md` to any documentation URL returns its source. |

`lib/llms.ts` builds all three. The index answers at the site root because that is where a client probes first, and `/docs/llms.txt` redirects there.

Links in these files point at `.md` endpoints rather than at pages, so following one returns Markdown rather than an application shell. A page and a route handler cannot both answer at one path in the App Router, so `next.config.mjs` rewrites `/docs/x.md` to the handler in `app/md`. That rewrite is in `afterFiles`, which runs after the filesystem check, so it cannot shadow a file under `public`. It runs before the dynamic routes, so it wins over the documentation catch-all.

Relative Markdown links are rewritten to absolute URLs in both files, because a relative path resolves to nothing once a page is served on its own or concatenated with every other page. `lib/doc-links.ts` holds that resolution, and the rendered page uses it too.

`llms-full.txt` reproduces every page in full and filters nothing out. The build gate in `scripts/check-llms.mjs` asserts that. The index follows the convention's shape, every link in it is absolute and reaches a Markdown endpoint that was built, every entry carries a note, and the complete text holds every page its own contents list names, with no relative link left in it.

## Local Development

Use Node.js 24. Fumadocs, Next.js, CI, and Vercel use the major version pinned in `package.json`. The documentation layer uses `fumadocs-core` and `@fumadocs/local-md` only, styled by one hand-written stylesheet (`app/globals.css`) and components in `components/` (`site-header.tsx`, `site-footer.tsx`, `docs-shell.tsx`, `docs-sidebar.tsx`, `docs-article.tsx`, `search-box.tsx`, `code-copy-button.tsx`, `copy-button.tsx`, `docs-not-found.tsx`).

Produce every `package-lock.json` change on Linux. npm on Windows leaves out packages that optional WebAssembly dependencies need on Linux, so a lockfile written there makes `npm ci` fail in CI and on Vercel.

To add or update a dependency, edit `package.json`, then run `docker run --rm -v "$PWD:/w" -w /w node:24 npm install --package-lock-only --ignore-scripts` from `site/`. On Windows, install with `npm ci`, not `npm install`, so the lockfile is not rewritten.

```bash
npm ci
npm run dev
```

Open `http://localhost:3100`. The build command is `npm run build`, and `npm run check` then runs the rendered-link, plain-text, search-trace, and title checks against its output. The production server is `npm run start`. Run `npm run lint` for the Next.js ESLint rules and `npm run typecheck` for a standalone TypeScript check.

The build and release workflows run a clean install, a high-severity dependency audit, linting, type checking, the production build, the rendered-link gate, the plain-text gate, the search-trace gate, and the title gate. The title gate in `scripts/check-titles.mjs` asserts the naming model. Each folder label matches its index page H1, and page names are unique within a top-level section. Changes under `site/` trigger the build workflow.

## Vercel Deployment

Import the `litenova/LiteBus` repository as a Vercel project and set **Root Directory** to `site`. Leave the output directory unset so the Next.js preset can manage `.next`. The project does not require environment variables.

The checked-in `vercel.json` pins the Next.js framework preset and uses `npm ci` followed by `npm run build`. Vercel does not run `npm run check`. On Vercel, Next.js writes its build output through the Vercel adapter rather than the `.next/server/app` layout the checks read, so the CI Documentation job runs them instead. Vercel reads Node.js 24 from `package.json`. Deployments are skipped when a commit has no changes under `site/`. A manual redeployment can bypass the ignored build step in the Vercel dashboard.

Use these project settings:

| Setting | Value |
|---------|-------|
| Root Directory | `site` |
| Framework Preset | Next.js |
| Install Command | `npm ci` from `vercel.json` |
| Build Command | `npm run build` from `vercel.json` |
| Output Directory | Framework default |
| Node.js Version | `24.x` from `package.json` |
