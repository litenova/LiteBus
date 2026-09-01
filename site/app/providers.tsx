'use client';

import { usePathname } from 'next/navigation';
import { RootProvider } from 'fumadocs-ui/provider/next';
import { docsVersions, versionFromPathname } from '@/lib/versions';

const searchTags = docsVersions.map((version) => ({
  name: `${version.label} - ${version.description}`,
  value: version.id,
}));

/**
 * Application providers.
 *
 * Search is scoped to the version the reader is currently on, so a question asked from one version's pages is answered
 * from that version's pages. The tag list stays visible in the dialog so the scope can be widened deliberately.
 */
export function Providers({ children }: Readonly<{ children: React.ReactNode }>) {
  const version = versionFromPathname(usePathname());

  return (
    <RootProvider
      search={{
        options: {
          defaultTag: version.id,
          tags: searchTags,
        },
      }}
    >
      {children}
    </RootProvider>
  );
}
