import type { MetadataRoute } from 'next';
import { getAllSources } from '@/lib/source';
import { siteUrl } from '@/lib/site';
import { versionBasePath } from '@/lib/versions';

export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
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

  // A pre-release line is excluded, matching the `noindex` its pages carry. Listing pages a crawler is told to skip
  // only invites the coverage warnings that make the rest of the sitemap harder to read.
  const docRoutes: MetadataRoute.Sitemap = (await getAllSources())
    .filter(({ version }) => version.status !== 'prerelease')
    .flatMap(({ version, source }) => {
      const root = versionBasePath(version);

      return source.getPages().map((page) => ({
        url: new URL(page.url, siteUrl).toString(),
        lastModified,
        changeFrequency: 'weekly' as const,
        priority: page.url === root ? 0.9 : 0.7,
      }));
    });

  return [...staticRoutes, ...docRoutes];
}
