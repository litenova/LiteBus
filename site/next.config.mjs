/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  poweredByHeader: false,
  outputFileTracingIncludes: {
    '/api/search': ['./content/docs/**/*'],
  },
};

export default nextConfig;
