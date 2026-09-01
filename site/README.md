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

### Two lines at once

While a major line is in pre-release, two lines are live: the released one still takes patches on `main`, and the next
one is developed on its own branch. Only the release branch carries both, because it holds the working documentation
for the new line and a snapshot of the released one. So the site deploys from the release branch for as long as that
is true, and returns to `main` when the line merges and `main` carries both again.

That makes the release branch's snapshot the published documentation for the released line, so a documentation fix
that lands on `main` has to reach the snapshot as well. The version's `tracks` field names the branch that happens on,
and `scripts/Test-VersionSnapshots.ps1` compares the two and reports the files that differ. The build workflow runs it
and raises a warning rather than failing, since the missing fix is work on another branch. To apply one:

```bash
git checkout origin/main -- site/content/docs/<file>
git mv -f site/content/docs/<file> site/content/versions/<id>/<file>
```

Drop `tracks` from the entry once the line stops receiving fixes. The snapshot is then final, and the check skips it.

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
