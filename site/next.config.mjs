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
  },
  async headers() {
    return [
      {
        source: '/:path*',
        headers: securityHeaders,
      },
    ];
  },
  async redirects() {
    // A version identifier is a plausible thing to type or guess on its own, and `/docs/v7` is the shape a reader
    // familiar with the unprefixed default would try first. Both land on that version's documentation index rather
    // than on a not-found page.
    return prefixedVersions.flatMap((version) => [
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
    ]);
  },
};

export default nextConfig;
