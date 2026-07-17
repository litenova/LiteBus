import { localMd } from '@fumadocs/local-md';
import { dynamicLoader } from 'fumadocs-core/source/dynamic';

const docs = localMd({
  dir: 'content/docs',
});

if (process.env.NODE_ENV === 'development') {
  void docs.devServer();
}

const docsLoader = dynamicLoader(docs.dynamicSource(), {
  baseUrl: '/docs',
});

export async function getSource() {
  return docsLoader.get();
}
