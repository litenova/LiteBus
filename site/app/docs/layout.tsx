import { DocsLayout } from 'fumadocs-ui/layouts/docs';
import { getSource } from '@/lib/source';

export default async function DocsRootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  const docs = await getSource();

  return (
    <DocsLayout
      tree={docs.getPageTree()}
      nav={{
        title: 'LiteBus',
        url: '/',
      }}
    >
      {children}
    </DocsLayout>
  );
}
