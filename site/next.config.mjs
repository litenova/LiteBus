const contentSecurityPolicy = [
  "default-src 'self'",
  "base-uri 'self'",
  "form-action 'self'",
  "frame-ancestors 'none'",
  "object-src 'none'",
  "img-src 'self' data: https:",
  "font-src 'self' data:",
  "style-src 'self' 'unsafe-inline'",
  "script-src 'self' 'unsafe-inline'",
  "connect-src 'self'",
  'upgrade-insecure-requests',
].join('; ');

const securityHeaders = [
  { key: 'Content-Security-Policy', value: contentSecurityPolicy },
  { key: 'Strict-Transport-Security', value: 'max-age=63072000; includeSubDomains; preload' },
  { key: 'X-Content-Type-Options', value: 'nosniff' },
  { key: 'X-Frame-Options', value: 'DENY' },
  { key: 'Referrer-Policy', value: 'strict-origin-when-cross-origin' },
  { key: 'X-DNS-Prefetch-Control', value: 'on' },
  {
    key: 'Permissions-Policy',
    value: 'camera=(), microphone=(), geolocation=(), interest-cohort=(), browsing-topics=()',
  },
];

/** @type {import('next').NextConfig} */
const nextConfig = {
  // Ondine loads its compiler WebAssembly from a file URL, which only works when Node loads the package unbundled.
  serverExternalPackages: ['ondine'],
  // The repository root AGENTS.md is the agent guidance; Next must not write a second one into site/.
  agentRules: false,
  reactStrictMode: true,
  poweredByHeader: false,
  outputFileTracingIncludes: {
    // The search route reads the documentation's Markdown at request time, so the content root has to be traced into
    // the serverless bundle. Keep this in step with `docsContentDir` in lib/site.ts.
    '/api/search': ['./content/docs/**/*'],
    // The plain-text views read the same Markdown. They are prerendered, so this only matters for the `.md` route
    // reached by a path that was not in `generateStaticParams`, but the cost of tracing them is a build-time copy.
    '/llms.txt': ['./content/docs/**/*'],
    '/llms-full.txt': ['./content/docs/**/*'],
    '/md/[...path]': ['./content/docs/**/*'],
  },
  async headers() {
    return [
      {
        source: '/:path*',
        headers: securityHeaders,
      },
    ];
  },
  async rewrites() {
    // Appending `.md` to a documentation URL returns that page's Markdown source, which is how the llms.txt
    // convention offers a clean copy of a rendered page. A page and a route handler cannot both answer at one path,
    // so the extension is rewritten onto the handler in app/md. Returning an array puts this in `afterFiles`, which
    // runs after the filesystem check and so cannot shadow a real file under `public`, and before the dynamic routes,
    // so it wins over the documentation catch-all. The handler rejects a path that is not a documentation path.
    return [
      {
        source: '/:path(.*).md',
        destination: '/md/:path',
      },
    ];
  },
  async redirects() {
    // The plain-text index answers at the site root, since that is where the convention has clients probe. The
    // prefixed form is the other reasonable guess, so it lands on the same file instead of on the documentation
    // catch-all. Keep in step with `llmsIndexPath` in lib/llms.ts.
    const plainTextIndexes = [
      { source: '/docs/llms.txt', destination: '/llms.txt', permanent: false },
      { source: '/docs/llms-full.txt', destination: '/llms-full.txt', permanent: false },
    ];

    // Old `/v7/` documentation links redirect to the same path without the prefix, so links written while the
    // documentation carried a version segment keep working.
    const legacyVersionPrefix = [
      { source: '/v7/docs', destination: '/docs', permanent: true },
      { source: '/v7/docs/:path*', destination: '/docs/:path*', permanent: true },
      { source: '/docs/v7/:path*', destination: '/docs/:path*', permanent: true },
      { source: '/v7', destination: '/docs', permanent: true },
    ];

    // Pages that moved, merged, or were deleted in the documentation restructure keep their old URL working. The old
    // `/docs/operations` overview is the one exception: it now serves the new operations overview, so a redirect there
    // would shadow it; the management-endpoints content it used to hold lives at `/docs/operations/management-endpoints`.
    const restructuredDocs = [
      { source: '/docs/concepts/domain-events-and-unit-of-work', destination: '/docs/reliable-messaging/domain-events-and-unit-of-work', permanent: true },
      { source: '/docs/concepts/recurring-commands', destination: '/docs/integrations/recurring-commands', permanent: true },
      { source: '/docs/reference/feature-index-v7', destination: '/docs/reference/feature-index', permanent: true },
      { source: '/docs/roadmap/marten-outbox-writer', destination: '/docs/integrations/outbox-marten-storage', permanent: true },
      { source: '/docs/reference/feature-index-v6', destination: '/docs/reference/feature-index', permanent: true },
      { source: '/docs/reference/semantic-validation', destination: '/docs/contributing/semantic-validation', permanent: true },
      { source: '/docs/migration/from-mediatr', destination: '/docs/migration/mediatr-differences', permanent: true },
      { source: '/docs/integrations/inbox-amqp', destination: '/docs/integrations/inbox-amqp-ingress', permanent: true },
      { source: '/docs/catalog/hosting/module-registry', destination: '/docs/catalog/runtime/module-registry', permanent: true },
      { source: '/docs/catalog/saga/processor-envelope-hooks', destination: '/docs/catalog/durable-core/processor-hooks', permanent: true },
    ];

    return [...plainTextIndexes, ...legacyVersionPrefix, ...restructuredDocs];
  },
};

export default nextConfig;
