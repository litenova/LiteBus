import Image from 'next/image';
import { DocsLayout } from 'fumadocs-ui/layouts/docs';
import { getSource } from '@/lib/source';
import type { DocsVersion } from '@/lib/versions';
import { VersionSwitcher } from '@/components/version-switcher';

/**
 * The documentation shell for one version: navigation, the version switcher, and the page tree of that version alone.
 *
 * Every version renders its own shell, so the sidebar never lists pages the reader cannot reach from where they are.
 */
export async function DocsShell({
  version,
  children,
}: Readonly<{ version: DocsVersion; children: React.ReactNode }>) {
  const docs = await getSource(version.id);

  return (
    <DocsLayout
      tree={docs.getPageTree()}
      nav={{
        title: (
          <span className="litebus-nav-brand">
            <Image src="/icon.svg" alt="" width={24} height={24} />
            <span>LiteBus</span>
          </span>
        ),
        url: '/',
      }}
      sidebar={{
        banner: <VersionSwitcher current={version.id} />,
      }}
    >
      {children}
    </DocsLayout>
  );
}
