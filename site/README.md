# LiteBus documentation site

This is the Fumadocs site for LiteBus. The Markdown source is copied from `../docs` so
the repository's source documentation remains available to package and architecture
workflows while the site can evolve its navigation and presentation independently.

## Local development

Use Node.js 22 or later. Fumadocs and Next.js use the version declared in `package.json`.
The lockfile overrides Next.js's bundled PostCSS to the patched `8.5.10` release.

```bash
npm ci
npm run dev
```

Open `http://localhost:3000`. The build command is `npm run build` and the production
server is `npm run start`.
