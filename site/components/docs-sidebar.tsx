'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { Fragment } from 'react';
import { DocsDrawer } from '@/components/docs-drawer';

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
 * One labelled group of the curated navigation, with each link already resolved to a URL.
 */
export type CuratedGroup = {
  readonly heading: string;
  readonly links: readonly { readonly label: string; readonly url: string }[];
};

/**
 * The documentation sidebar: the curated groups above the full page tree.
 *
 * The tree arrives as serialisable data from the docs shell because the page tree's names are React nodes, which the
 * server to client boundary cannot carry unchanged. The current page is highlighted from the pathname, so the sidebar
 * stays correct as the reader moves without another round trip to the server. Rendering goes through the docs drawer,
 * which also shows the mobile menu row with the current page path.
 */
export function DocsSidebar({
  tree,
  curated,
}: Readonly<{ tree: readonly SidebarNode[]; curated: readonly CuratedGroup[] }>) {
  const pathname = usePathname();

  const curatedUrls = new Set(curated.flatMap((group) => group.links.map((link) => link.url)));
  const inMore = !curatedUrls.has(pathname) && tree.some((node) => nodeContains(node, pathname));
  const found = menuPathFor(tree, pathname);

  // The drawer remounts for the new path, which closes it after navigating to a page.
  return (
    <DocsDrawer
      key={pathname}
      parent={found.parent}
      current={found.current}
      navigation={
        <>
          {curated.map((group, index) => {
            const body = (
              <>
                <h2 className="xhead">{group.heading}</h2>
                {group.links.map((link) => {
                  const current = link.url === pathname;

                  return (
                    <Link
                      key={link.url}
                      href={link.url}
                      className={current ? 'xrow leaf cur' : 'xrow leaf'}
                      aria-current={current ? 'page' : undefined}
                    >
                      {link.label}
                    </Link>
                  );
                })}
              </>
            );

            return index === 0 ? (
              <Fragment key={group.heading}>{body}</Fragment>
            ) : (
              <div key={group.heading} className="xgroup">
                {body}
              </div>
            );
          })}
          <details className="all" open={inMore}>
            <summary className="xhead">
              <span className="chev" aria-hidden="true">
                &gt;
              </span>
              All Pages
            </summary>
            <SidebarGroups nodes={tree} pathname={pathname} depth={0} />
          </details>
        </>
      }
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
          <Fragment key={`group-${index}`}>
            {group.heading === undefined ? null : <h2 className="xhead">{group.heading}</h2>}
            {group.nodes.map((node, nodeIndex) => (
              <SidebarNodeItem
                key={sidebarKey(node, nodeIndex)}
                node={node}
                pathname={pathname}
                depth={depth}
              />
            ))}
          </Fragment>
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
  const row = depth === 0 ? 'xrow leaf' : 'xrow xsub';

  if (page.external) {
    return (
      <a href={page.url} className={row}>
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
      {page.name}
    </Link>
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
          {children.map((child, index) => (
            <SidebarNodeItem
              key={sidebarKey(child, index)}
              node={child}
              pathname={pathname}
              depth={depth + 1}
            />
          ))}
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
