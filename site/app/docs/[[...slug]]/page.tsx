import { notFound } from 'next/navigation';
import { DocsBody, DocsPage } from 'fumadocs-ui/layouts/docs/page';
import { getSource } from '@/lib/source';

export default async function DocsPageRoute({
  params,
}: {
  params: Promise<{ slug?: string[] }>;
}) {
  const { slug = [] } = await params;
  const page = (await getSource()).getPage(slug);

  if (!page) {
    notFound();
  }

  const renderer = await page.data.load();
  const rendered = await renderer.render();

  return (
    <DocsPage toc={rendered.toc}>
      <DocsBody>{rendered.body}</DocsBody>
    </DocsPage>
  );
}
