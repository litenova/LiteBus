import { localMd } from '@fumadocs/local-md';
import { remarkOndine } from 'ondine/remark';
import { getSlugs } from 'fumadocs-core/source';
import { dynamicLoader } from 'fumadocs-core/source/dynamic';
import { pageSchema } from 'fumadocs-core/source/schema';
import { z } from 'zod';
import { extractDescription, extractTitle } from '@/lib/doc-summary';
import { rehypeCodeOptions } from '@/lib/highlight';
import { docsBasePath, docsContentDir } from '@/lib/site';

function createDocsLoader() {
  const docs = localMd({
    dir: docsContentDir,
    frontmatterSchema: pageSchema.extend({
      title: z.string().optional(),
    }),
    // Mermaid fences compile to inline SVG at build time, so pages ship no diagram renderer.
    mdOptions: { rehypeCodeOptions, remarkPlugins: [remarkOndine] },
  });

  if (process.env.NODE_ENV === 'development') {
    void docs.devServer();
  }

  const rawSource = docs.dynamicSource();

  // The sources carry no frontmatter, so the title and description are read out of the prose. See lib/doc-summary.ts
  // for why that is the arrangement rather than 240 hand-written frontmatter blocks.
  const docsSource = {
    ...rawSource,
    async files() {
      const files = await rawSource.files();

      return files.map((file) => {
        if (file.type !== 'page') {
          return file;
        }

        const title = extractTitle(file.data.content);
        const description = extractDescription(file.data.content);

        return {
          ...file,
          data: {
            ...file.data,
            ...(title === undefined ? {} : { title }),
            ...(description === undefined ? {} : { description }),
          },
        };
      });
    },
  } satisfies typeof rawSource;

  return dynamicLoader<typeof rawSource>(docsSource, {
    baseUrl: docsBasePath,
    slugs: (file) => {
      const slugs = getSlugs(file.path);

      return slugs.at(-1)?.toLowerCase() === 'readme' ? slugs.slice(0, -1) : slugs;
    },
  });
}

// One loader, created once per process. It owns a file watcher in development, so building it eagerly on every
// request would leak watchers and re-read the whole tree.
const loader = createDocsLoader();

/**
 * The one documentation set.
 */
export async function getSource() {
  return loader.get();
}

export type DocsSource = Awaited<ReturnType<typeof getSource>>;
