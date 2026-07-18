import type { MetadataRoute } from 'next';
import { siteDescription, siteName } from '@/lib/site';

export default function manifest(): MetadataRoute.Manifest {
  return {
    name: `${siteName} documentation`,
    short_name: siteName,
    description: siteDescription,
    start_url: '/',
    scope: '/',
    display: 'standalone',
    background_color: '#241f2b',
    theme_color: '#fdbf00',
    categories: ['developer', 'productivity', 'reference'],
    icons: [
      {
        src: '/icon.svg',
        type: 'image/svg+xml',
        sizes: 'any',
        purpose: 'any',
      },
      {
        src: '/icon.png',
        type: 'image/png',
        sizes: '128x128',
        purpose: 'any',
      },
    ],
  };
}
