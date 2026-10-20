'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { DocsDrawer } from '@/components/docs-drawer';
import { docsBasePath } from '@/lib/site';

/**
 * A sidebar page link, serialisable across the server to client boundary.
 */
export type SidebarPage = {
  readonly type: 'page';
  readonly name: string;
  readonly url: string;
  readonly external: boolean;
};

/**
 * A sidebar group heading, from a `---Group---` entry in the root `meta.json`.
 */
export type SidebarSeparator = {
  readonly type: 'separator';
  readonly name: string;
};

/**
 * A sidebar folder, opened when it contains the page being read.
 */
export type SidebarFolder = {
  readonly type: 'folder';
  readonly name: string;
  readonly index: SidebarPage | undefined;
  readonly children: readonly SidebarNode[];
};

/**
 * One node of the serialisable sidebar tree the docs shell passes down.
 */
export type SidebarNode = SidebarPage | SidebarSeparator | SidebarFolder;

/**
 * The documentation sidebar: the page tree, in the order and groups the content `meta.json` files declare.
 *
 * It is the only navigation. Every page is in it once, under the folder that holds it, so the sidebar, the breadcrumb
 * and the tab title all name a page the same way. The tree arrives as serialisable data from the docs shell because the
 * page tree's names are React nodes, which the server to client boundary cannot carry unchanged. The current page is
 * highlighted from the pathname, so the sidebar stays correct as the reader moves without another round trip to the
 * server. Rendering goes through the docs drawer, which also shows the mobile menu row with the current page path.
 */
export function DocsSidebar({ tree }: Readonly<{ tree: readonly SidebarNode[] }>) {
  const pathname = usePathname();
  const found = menuPathFor(tree, pathname);

  // The drawer remounts for the new path, which closes it after navigating to a page.
  return (
    <DocsDrawer
      key={pathname}
      parent={found.parent}
      current={found.current}
      navigation={<SidebarGroups nodes={tree} pathname={pathname} depth={0} />}
    />
  );
}

/**
 * The grouped page lists: one heading and list per separator, with leading pages listed unheaded.
 */
function SidebarGroups({
  nodes,
  pathname,
  depth,
}: Readonly<{ nodes: readonly SidebarNode[]; pathname: string; depth: number }>) {
  const groups: { heading: string | undefined; nodes: SidebarNode[] }[] = [];
  let current: { heading: string | undefined; nodes: SidebarNode[] } = {
    heading: undefined,
    nodes: [],
  };

  for (const node of nodes) {
    if (node.type === 'separator') {
      groups.push(current);
      current = { heading: node.name.length > 0 ? node.name : undefined, nodes: [] };
    } else {
      current.nodes.push(node);
    }
  }

  groups.push(current);

  return (
    <>
      {groups
        .filter((group) => group.nodes.length > 0)
        .map((group, index) => (
          <div key={`group-${index}`} className="xsec">
            {group.heading === undefined ? null : depth === 0 ? (
              <h2 className="xhead">{group.heading}</h2>
            ) : (
              <h3 className="xhead">{group.heading}</h3>
            )}
            {group.nodes.map((node, nodeIndex) => (
              <SidebarNodeItem
                key={sidebarKey(node, nodeIndex)}
                node={node}
                pathname={pathname}
                depth={depth}
              />
            ))}
          </div>
        ))}
    </>
  );
}

/**
 * A stable key for a sidebar node among its siblings.
 */
function sidebarKey(node: SidebarNode, index: number): string {
  if (node.type === 'page') {
    return node.url;
  }

  if (node.type === 'folder') {
    return `folder-${node.index?.url ?? node.name}-${index}`;
  }

  return `separator-${index}`;
}

/**
 * One sidebar node: a page link, a collapsible folder, or a nested group label.
 */
function SidebarNodeItem({
  node,
  pathname,
  depth,
}: Readonly<{ node: SidebarNode; pathname: string; depth: number }>) {
  if (node.type === 'page') {
    return <SidebarPageItem page={node} pathname={pathname} depth={depth} />;
  }

  if (node.type === 'folder') {
    return <SidebarFolderItem folder={node} pathname={pathname} depth={depth} />;
  }

  return <div className="xhead">{node.name}</div>;
}

/**
 * One sidebar page link, marked current when it is the page being read.
 */
function SidebarPageItem({
  page,
  pathname,
  depth,
}: Readonly<{ page: SidebarPage; pathname: string; depth: number }>) {
  const current = page.url === pathname;
  const row = depth === 0 ? 'xrow' : 'xrow xsub';
  // A top-level page fills the slot a folder uses for its chevron, so every top-level row starts at the same edge.
  const glyph = depth === 0 ? <PageGlyph home={page.url === docsBasePath} /> : null;

  if (page.external) {
    return (
      <a href={page.url} className={row}>
        {glyph}
        {page.name}
      </a>
    );
  }

  return (
    <Link
      href={page.url}
      className={current ? `${row} cur` : row}
      aria-current={current ? 'page' : undefined}
    >
      {glyph}
      {page.name}
    </Link>
  );
}

/**
 * The leading glyph of a top-level page row: a house for the documentation home, a page for any other page.
 */
function PageGlyph({ home }: Readonly<{ home: boolean }>) {
  return (
    <span className="chev" aria-hidden="true">
      <svg className="xglyph" viewBox="0 0 16 16" width="12" height="12">
        {home ? (
          <path d="M2.5 7.2 8 2.5l5.5 4.7M4 6v7.5h3V10h2v3.5h3V6" />
        ) : (
          <path d="M4 1.75h5.25L12.5 5v9.25H4zM9 1.75V5.5h3.5" />
        )}
      </svg>
    </span>
  );
}

/**
 * One sidebar folder: the index page is the row link, and the folder opens when it holds the current page.
 */
function SidebarFolderItem({
  folder,
  pathname,
  depth,
}: Readonly<{ folder: SidebarFolder; pathname: string; depth: number }>) {
  const indexUrl = folder.index?.url;
  const children = folder.children.filter(
    (child) => child.type !== 'page' || child.url !== indexUrl,
  );
  const current = indexUrl !== undefined && indexUrl === pathname;
  const row = depth === 0 ? 'xrow' : 'xrow xsub';

  return (
    <details open={folderContains(folder, pathname)}>
      <summary className={current ? `${row} cur` : row}>
        <span className="chev" aria-hidden="true">
          &gt;
        </span>
        {folder.index === undefined ? (
          folder.name
        ) : (
          <Link href={folder.index.url} aria-current={current ? 'page' : undefined}>
            {folder.index.name}
          </Link>
        )}
      </summary>
      {children.length === 0 ? null : (
        <div className="xkids">
          <SidebarGroups nodes={children} pathname={pathname} depth={depth + 1} />
        </div>
      )}
    </details>
  );
}

/**
 * Whether any page under a node is the page being read.
 */
function nodeContains(node: SidebarNode, pathname: string): boolean {
  if (node.type === 'page') {
    return node.url === pathname;
  }

  if (node.type === 'folder') {
    return folderContains(node, pathname);
  }

  return false;
}

/**
 * Whether a folder holds the page being read, counting its index page.
 */
function folderContains(folder: SidebarFolder, pathname: string): boolean {
  if (folder.index !== undefined && folder.index.url === pathname) {
    return true;
  }

  return folder.children.some((child) => nodeContains(child, pathname));
}

/**
 * The mobile menu path for the page being read: its parent section and its own name.
 *
 * The folders above the current page in the tree decide the path, matching the article crumbs. A page the tree does
 * not reach falls back to its last URL segment, and the documentation index falls back to `Docs`.
 */
function menuPathFor(
  nodes: readonly SidebarNode[],
  pathname: string,
): { readonly parent: string | undefined; readonly current: string } {
  const found = findMenuPath(nodes, pathname, []);

  if (found !== undefined) {
    return found.parent === found.current
      ? { parent: undefined, current: found.current }
      : found;
  }

  const segments = pathname.split('/').filter((segment) => segment.length > 0);

  if (segments[0] === 'docs') {
    segments.shift();
  }

  const last = segments[segments.length - 1];

  if (last === undefined) {
    return { parent: undefined, current: 'Docs' };
  }

  const words = last.replaceAll('-', ' ');

  return { parent: undefined, current: words.slice(0, 1).toUpperCase() + words.slice(1) };
}

/**
 * The menu path for the page at `pathname`, tracking the folders above each node.
 */
function findMenuPath(
  nodes: readonly SidebarNode[],
  pathname: string,
  folders: readonly string[],
): { readonly parent: string | undefined; readonly current: string } | undefined {
  for (const node of nodes) {
    if (node.type === 'page') {
      if (node.url === pathname) {
        return { parent: folders[folders.length - 1], current: node.name };
      }
    } else if (node.type === 'folder') {
      if (node.index !== undefined && node.index.url === pathname) {
        return {
          parent: folders[folders.length - 1] ?? node.name,
          current: node.index.name,
        };
      }

      const found = findMenuPath(node.children, pathname, [...folders, node.name]);

      if (found !== undefined) {
        return found;
      }
    }
  }

  return undefined;
}
