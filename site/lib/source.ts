import { localMd } from '@fumadocs/local-md';
import { getSlugs } from 'fumadocs-core/source';
import { dynamicLoader } from 'fumadocs-core/source/dynamic';
import { pageSchema } from 'fumadocs-core/source/schema';
import { z } from 'zod';
import { extractDescription, extractTitle } from '@/lib/doc-summary';
import {
  type DocsVersion,
  defaultVersion,
  docsVersions,
  findVersion,
  versionBasePath,
} from '@/lib/versions';

function createDocsLoader(version: DocsVersion) {
  const docs = localMd({
    dir: version.dir,
    frontmatterSchema: pageSchema.extend({
      title: z.string().optional(),
    }),
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
    baseUrl: versionBasePath(version),
    slugs: (file) => {
      const slugs = getSlugs(file.path);

      return slugs.at(-1)?.toLowerCase() === 'readme' ? slugs.slice(0, -1) : slugs;
    },
  });
}

// One loader per version, created once per process. Each loader owns a file watcher in development, so building them
// eagerly on every request would leak watchers and re-read the whole tree.
const loaders = new Map(
  docsVersions.map((version) => [version.id, createDocsLoader(version)] as const),
);

export async function getSource(versionId: string = defaultVersion.id) {
  const loader = loaders.get(versionId);

  if (loader === undefined) {
    throw new Error(`'${versionId}' is not a documentation version declared in lib/versions.ts.`);
  }

  return loader.get();
}

export type DocsSource = Awaited<ReturnType<typeof getSource>>;

/** Every version paired with its loaded source, for the routes that have to walk all of them. */
export async function getAllSources(): Promise<{ version: DocsVersion; source: DocsSource }[]> {
  return Promise.all(
    docsVersions.map(async (version) => ({ version, source: await getSource(version.id) })),
  );
}

/** Resolves the version named by a route segment, or throws if the segment names nothing. */
export function requireVersion(id: string): DocsVersion {
  const version = findVersion(id);

  if (version === undefined) {
    throw new Error(`'${id}' is not a documentation version declared in lib/versions.ts.`);
  }

  return version;
}
