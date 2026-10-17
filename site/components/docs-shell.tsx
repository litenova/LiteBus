import type { Item, Node, Root } from 'fumadocs-core/page-tree';
import { DocsSidebar, type CuratedGroup, type SidebarNode, type SidebarPage } from '@/components/docs-sidebar';
import { SiteHeader } from '@/components/site-header';
import { getSource } from '@/lib/source';
import { curatedNavigation } from '@/lib/docs-nav';
import { docsBasePath } from '@/lib/site';

/**
 * The display text of a page tree node.
 *
 * These trees always name nodes with plain strings, so anything else falls back to the given default rather than
 * carrying a React node across the server to client boundary.
 */
function nodeText(name: unknown, fallback: string): string {
  if (typeof name === 'string' && name.length > 0) {
    return name;
  }

  if (typeof name === 'number') {
    return String(name);
  }

  return fallback;
}

/**
 * A page tree item as serialisable sidebar data.
 */
function toSidebarPage(item: Item): SidebarPage {
  return {
    type: 'page',
    name: nodeText(item.name, item.url),
    url: item.url,
    external: item.external ?? false,
  };
}

/**
 * A page tree node as serialisable sidebar data.
 */
function toSidebarNode(node: Node): SidebarNode {
  if (node.type === 'page') {
    return toSidebarPage(node);
  }

  if (node.type === 'separator') {
    return { type: 'separator', name: nodeText(node.name, '') };
  }

  return {
    type: 'folder',
    name: nodeText(node.name, 'Documentation'),
    index: node.index === undefined ? undefined : toSidebarPage(node.index),
    children: node.children.map(toSidebarNode),
  };
}

/**
 * A page tree as the serialisable list the sidebar renders.
 */
function toSidebarTree(tree: Root): SidebarNode[] {
  return tree.children.map(toSidebarNode);
}

/**
 * The documentation shell: the header above the explorer, page, and outline grid.
 *
 * The page-dependent chrome, the outline and the status bar, renders where its data is: the article and the
 * not-found page each render their own status bar below the grid.
 */
export async function DocsShell({ children }: Readonly<{ children: React.ReactNode }>) {
  const tree = toSidebarTree((await getSource()).getPageTree());
  const curated: CuratedGroup[] = curatedNavigation.map((group) => ({
    heading: group.heading,
    links: group.links.map((link) => ({
      label: link.label,
      url: [docsBasePath, ...(link.slug.length === 0 ? [] : link.slug.split('/'))].join('/'),
    })),
  }));

  return (
    <>
      <a className="skip-link" href="#main">
        Skip to content
      </a>
      <div className="app">
        <SiteHeader current="docs" />
        <div className="grow docs">
          <DocsSidebar tree={tree} curated={curated} />
          {children}
        </div>
      </div>
    </>
  );
}
