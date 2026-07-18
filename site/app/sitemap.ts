import type { MetadataRoute } from 'next';
import { getSource } from '@/lib/source';
import { siteUrl } from '@/lib/site';

export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const source = await getSource();
  const lastModified = new Date();

  const staticRoutes: MetadataRoute.Sitemap = [
    {
      url: `${siteUrl}/`,
      lastModified,
      changeFrequency: 'weekly',
      priority: 1,
    },
    {
      url: `${siteUrl}/privacy`,
      lastModified,
      changeFrequency: 'yearly',
      priority: 0.2,
    },
  ];

  const docRoutes: MetadataRoute.Sitemap = source.getPages().map((page) => ({
    url: new URL(page.url, siteUrl).toString(),
    lastModified,
    changeFrequency: 'weekly',
    priority: page.url === '/docs' ? 0.9 : 0.7,
  }));

  return [...staticRoutes, ...docRoutes];
}
