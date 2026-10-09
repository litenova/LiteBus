import type { Item, Node, Root } from 'fumadocs-core/page-tree';
import { DocsSidebar, type CuratedGroup, type SidebarNode, type SidebarPage } from '@/components/docs-sidebar';
import { SiteFooter } from '@/components/site-footer';
import { SiteHeader } from '@/components/site-header';
import { getSource } from '@/lib/source';
import { curatedNavigation } from '@/lib/docs-nav';
import { versionHref, type DocsVersion } from '@/lib/versions';

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
 * The documentation shell for one version: header, sidebar, and footer around the page.
 *
 * Every version renders its own shell, so the sidebar never lists pages the reader cannot reach from where they are.
 */
export async function DocsShell({
  version,
  children,
}: Readonly<{ version: DocsVersion; children: React.ReactNode }>) {
  const tree = toSidebarTree((await getSource(version.id)).getPageTree());
  const curated: CuratedGroup[] | undefined = curatedNavigation[version.id]?.map((group) => ({
    heading: group.heading,
    links: group.links.map((link) => ({
      label: link.label,
      url: versionHref(version, link.slug.length === 0 ? [] : link.slug.split('/')),
    })),
  }));

  return (
    <>
      <a className="skip-link" href="#main">
        Skip to content
      </a>
      <div className="wrap wide">
        <SiteHeader current="docs" />
        <div className="docs">
          {children}
          <DocsSidebar tree={tree} curated={curated} versionId={version.id} />
        </div>
        <SiteFooter />
      </div>
    </>
  );
}
