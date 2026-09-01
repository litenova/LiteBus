# LiteBus documentation site

This is the Fumadocs site for LiteBus. It serves several documentation versions from one
deployment, so a reader can follow documentation for the release they have installed
rather than for the one under development.

Each page title is derived from the first level-one heading in its Markdown source.
Files named `README.md` are exposed as their directory index, so
`getting-started/README.md` is served at `/docs/getting-started`.
Relative Markdown links are resolved through the Fumadocs source loader. The production
build rejects rendered local links that still contain a `.md` source-file suffix.
The search route compiles Markdown at runtime, so its narrow Next.js output-file trace
includes every version's content directory for serverless deployments. The build verifies
that every Markdown source file is present in that trace.

## Documentation versions

`versions.json` declares every version the site serves. Exactly one entry is the default,
and it is the latest stable release.

| Directory | Holds |
|-----------|-------|
| `content/docs` | The working documentation, tracking the source tree in this repository. It describes the version under development, and it is the only tree the repository documentation gates check. |
| `content/versions/<id>` | A frozen snapshot of a released line. Snapshots are never edited in place, and the repository linters leave them alone because their snippets reference source that has since moved. |

The default version is served unprefixed at `/docs`, so the canonical documentation URL
does not move when a release is cut and existing links keep working. Every other version
carries its identifier as the first path segment, such as `/v7/docs/getting-started`.

A version whose `status` is `prerelease` is excluded from the sitemap and its pages carry
`noindex`, so a search engine does not offer documentation for a package most readers
should not install yet. Its pages also carry a notice pointing at the stable version.

The sidebar version switcher keeps the reader on the same page across versions. A page
that exists in only one version lands on the documentation not-found boundary, which
offers the versions that do have it.

Search is scoped to the version the reader is on. Each version has its own index, and the
search dialog sends the version identifier as its tag; the search route uses that tag to
pick an index rather than to filter inside one.

A version's `ref` is the git ref its Markdown sources live on, used for fallback links into the repository. A line
still developed on its own release branch names that branch, and becomes `main` when the line merges.

### A line that has not merged yet

The production site deploys from `main`, so a version whose sources live on an unmerged release branch is not on
litebus.io. Its pages build and are checked on every push to that branch, and Vercel publishes them as a branch
preview deployment; alias that deployment if the pre-release needs a stable URL before it merges. The version appears
at its own path on litebus.io as soon as the branch merges, with no further change to `versions.json`.

### Cutting a release

1. Copy `content/docs` to `content/versions/<previous id>` if that line has no snapshot yet.
2. Move `"default": true` in `versions.json` to the entry for the newly released line.
3. Set that entry's `status` to `stable` and update its `release` and `ref`.

Bump a pre-release entry's `release` for each preview, since it is the version the switcher shows.

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
linting, type checking, the production build, the rendered-link gate, and the search-trace
gate. Changes under `site/` trigger the build workflow.

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
