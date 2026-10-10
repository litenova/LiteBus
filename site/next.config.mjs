import { readFile } from 'node:fs/promises';

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

const { versions } = JSON.parse(await readFile(new URL('./versions.json', import.meta.url), 'utf8'));
const prefixedVersions = versions.filter((version) => !version.default);

/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  poweredByHeader: false,
  outputFileTracingIncludes: {
    // The search route reads every version's Markdown at request time, so each content root has to be traced into the
    // serverless bundle. Keep this in step with `dir` in lib/versions.ts.
    '/api/search': ['./content/docs/**/*', './content/versions/**/*'],
    // The plain-text views read the same Markdown. They are prerendered, so this only matters for the `.md` route
    // reached by a path that was not in `generateStaticParams`, but the cost of tracing them is a build-time copy.
    '/llms.txt': ['./content/docs/**/*', './content/versions/**/*'],
    '/llms-full.txt': ['./content/docs/**/*', './content/versions/**/*'],
    '/[version]/docs/llms.txt': ['./content/docs/**/*', './content/versions/**/*'],
    '/[version]/docs/llms-full.txt': ['./content/docs/**/*', './content/versions/**/*'],
    '/md/[...path]': ['./content/docs/**/*', './content/versions/**/*'],
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
    // The plain-text index for the default version answers at the site root, since that is where the convention has
    // clients probe. The prefixed form is the other reasonable guess, so it lands on the same file instead of on the
    // documentation catch-all. Keep in step with `llmsIndexPath` in lib/llms.ts.
    const plainTextIndexes = [
      { source: '/docs/llms.txt', destination: '/llms.txt', permanent: false },
      { source: '/docs/llms-full.txt', destination: '/llms-full.txt', permanent: false },
    ];

    // A version identifier is a plausible thing to type or guess on its own, and `/docs/v7` is the shape a reader
    // familiar with the unprefixed default would try first. Both land on that version's documentation index rather
    // than on a not-found page.
    return [
      ...plainTextIndexes,
      ...prefixedVersions.flatMap((version) => [
        {
          source: `/${version.id}`,
          destination: `/${version.id}/docs`,
          permanent: false,
        },
        {
          source: `/docs/${version.id}/:path*`,
          destination: `/${version.id}/docs/:path*`,
          permanent: false,
        },
        {
          source: `/docs/${version.id}`,
          destination: `/${version.id}/docs`,
          permanent: false,
        },
      ]),
    ];
  },
};

export default nextConfig;
