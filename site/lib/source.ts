import { localMd } from '@fumadocs/local-md';
import { getSlugs } from 'fumadocs-core/source';
import { dynamicLoader } from 'fumadocs-core/source/dynamic';
import { pageSchema } from 'fumadocs-core/source/schema';
import { z } from 'zod';

const docs = localMd({
  dir: 'content/docs',
  frontmatterSchema: pageSchema.extend({
    title: z.string().optional(),
  }),
});

if (process.env.NODE_ENV === 'development') {
  void docs.devServer();
}

const rawSource = docs.dynamicSource();
const docsSource = {
  ...rawSource,
  async files() {
    const files = await rawSource.files();

    return files.map((file) => {
      if (file.type !== 'page') {
        return file;
      }

      const title = /^#\s+(.+?)\s*$/m.exec(file.data.content)?.[1];

      return title === undefined
        ? file
        : {
            ...file,
            data: {
              ...file.data,
              title,
            },
          };
    });
  },
} satisfies typeof rawSource;

const docsLoader = dynamicLoader<typeof rawSource>(docsSource, {
  baseUrl: '/docs',
  slugs: (file) => {
    const slugs = getSlugs(file.path);

    return slugs.at(-1)?.toLowerCase() === 'readme' ? slugs.slice(0, -1) : slugs;
  },
});

export async function getSource() {
  return docsLoader.get();
}
