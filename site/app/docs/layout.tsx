import Image from 'next/image';
import { DocsLayout } from 'fumadocs-ui/layouts/docs';
import { getSource } from '@/lib/source';

export default async function DocsRootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  const docs = await getSource();

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
    >
      {children}
    </DocsLayout>
  );
}
