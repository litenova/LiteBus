# LiteBus documentation site

This is the Fumadocs site for LiteBus. The Markdown source is copied from `../docs` so
the repository's source documentation remains available to package and architecture
workflows while the site can evolve its navigation and presentation independently.

Each page title is derived from the first level-one heading in its Markdown source.
Files named `README.md` are exposed as their directory index, so
`docs/getting-started/README.md` is served at `/docs/getting-started`.
Relative Markdown links are resolved through the Fumadocs source loader. The production
build rejects rendered local links that still contain a `.md` source-file suffix.
The search route compiles Markdown at runtime, so its narrow Next.js output-file trace
includes `content/docs` for serverless deployments. The build verifies that every
Markdown source file is present in that trace.

## Local development

Use Node.js 24. Fumadocs, Next.js, CI, and Vercel use the major version pinned in
`package.json`.
The lockfile overrides Next.js's bundled PostCSS to the patched `8.5.10` release.
Tailwind CSS 4 and its PostCSS plugin compile the Fumadocs preset and application styles.
Regenerate `package-lock.json` on Linux when optional native dependencies change so
`npm ci` remains portable between contributor workstations, CI, and Vercel.

```bash
npm ci
npm run dev
```

Open `http://localhost:3000`. The build command is `npm run build` and the production
server is `npm run start`. Run `npm run lint` for the Next.js ESLint rules and
`npm run typecheck` for a standalone TypeScript check.

The build and release workflows run a clean install, a high-severity dependency audit,
linting, type checking, the production build, and the rendered-link gate. Changes under
`site/` or `Roadmap/` trigger the build workflow. A separate content-mirror gate rejects
missing, stale, or orphaned site pages before the Fumadocs build starts.

## Vercel deployment

Import the `litenova/LiteBus` repository as a Vercel project and set **Root Directory** to
`site`. Leave the output directory unset so the Next.js preset can manage `.next`. The
project does not require environment variables.

The checked-in `vercel.json` pins the Next.js framework preset and uses `npm ci` followed
by `npm run build`. The build includes the rendered-link and search-trace checks. Vercel
reads Node.js 24 from `package.json`. Deployments are skipped when a commit has no changes
under `site/`; a manual redeployment can bypass the ignored build step in the Vercel
dashboard.

Use these project settings:

| Setting | Value |
|---------|-------|
| Root Directory | `site` |
| Framework Preset | Next.js |
| Install Command | `npm ci` from `vercel.json` |
| Build Command | `npm run build` from `vercel.json` |
| Output Directory | Framework default |
| Node.js Version | `24.x` from `package.json` |
