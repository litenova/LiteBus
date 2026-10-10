'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { Fragment } from 'react';
import { SearchBox } from '@/components/search-box';
import { VersionSwitcher } from '@/components/version-switcher';

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
 * One labelled group of the curated navigation, with each link already resolved to a URL for the version.
 */
export type CuratedGroup = {
  readonly heading: string;
  readonly links: readonly { readonly label: string; readonly url: string }[];
};

/**
 * The documentation sidebar for one version: search, the version switcher, and that version's page tree.
 *
 * The tree arrives as serialisable data from the docs shell because the page tree's names are React nodes, which the
 * server to client boundary cannot carry unchanged. The current page is highlighted from the pathname, so the sidebar
 * stays correct as the reader moves without another round trip to the server.
 */
export function DocsSidebar({
  tree,
  curated,
  versionId,
}: Readonly<{ tree: readonly SidebarNode[]; curated: readonly CuratedGroup[] | undefined; versionId: string }>) {
  const pathname = usePathname();

  if (curated === undefined) {
    return (
      <nav className="sidebar" id="docs-nav" aria-label="Documentation">
        <SearchBox versionId={versionId} />
        <VersionSwitcher current={versionId} />
        <SidebarGroups nodes={tree} pathname={pathname} />
      </nav>
    );
  }

  const curatedUrls = new Set(curated.flatMap((group) => group.links.map((link) => link.url)));
  const inMore = !curatedUrls.has(pathname) && tree.some((node) => nodeContains(node, pathname));

  return (
    <nav className="sidebar" id="docs-nav" aria-label="Documentation">
      <SearchBox versionId={versionId} />
      {curated.map((group) => (
        <Fragment key={group.heading}>
          <h2>{group.heading}</h2>
          <ul>
            {group.links.map((link) => (
              <li key={link.url}>
                <Link href={link.url} aria-current={link.url === pathname ? 'page' : undefined}>
                  {link.label}
                </Link>
              </li>
            ))}
          </ul>
        </Fragment>
      ))}
      <details className="sidebar-more" open={inMore}>
        <summary>All Pages</summary>
        <SidebarGroups nodes={tree} pathname={pathname} />
      </details>
      <VersionSwitcher current={versionId} />
    </nav>
  );
}

/**
 * The grouped page lists: one heading and list per separator, with leading pages listed unheaded.
 */
function SidebarGroups({
  nodes,
  pathname,
}: Readonly<{ nodes: readonly SidebarNode[]; pathname: string }>) {
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
            {group.heading === undefined ? null : <h2>{group.heading}</h2>}
            <ul>
              {group.nodes.map((node, nodeIndex) => (
                <SidebarNodeItem key={sidebarKey(node, nodeIndex)} node={node} pathname={pathname} />
              ))}
            </ul>
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
}: Readonly<{ node: SidebarNode; pathname: string }>) {
  if (node.type === 'page') {
    return <SidebarPageItem page={node} pathname={pathname} />;
  }

  if (node.type === 'folder') {
    return <SidebarFolderItem folder={node} pathname={pathname} />;
  }

  return (
    <li>
      <span>{node.name}</span>
    </li>
  );
}

/**
 * One sidebar page link, marked current when it is the page being read.
 */
function SidebarPageItem({
  page,
  pathname,
}: Readonly<{ page: SidebarPage; pathname: string }>) {
  if (page.external) {
    return (
      <li>
        <a href={page.url}>{page.name}</a>
      </li>
    );
  }

  return (
    <li>
      <Link href={page.url} aria-current={page.url === pathname ? 'page' : undefined}>
        {page.name}
      </Link>
    </li>
  );
}

/**
 * One sidebar folder: the index page is the summary link, and the folder opens when it holds the current page.
 */
function SidebarFolderItem({
  folder,
  pathname,
}: Readonly<{ folder: SidebarFolder; pathname: string }>) {
  const indexUrl = folder.index?.url;
  const children = folder.children.filter(
    (child) => child.type !== 'page' || child.url !== indexUrl,
  );

  return (
    <li>
      <details open={folderContains(folder, pathname)}>
        <summary>
          {folder.index === undefined ? (
            folder.name
          ) : (
            <Link
              href={folder.index.url}
              aria-current={folder.index.url === pathname ? 'page' : undefined}
            >
              {folder.index.name}
            </Link>
          )}
        </summary>
        <ul>
          {children.map((child, index) => (
            <SidebarNodeItem key={sidebarKey(child, index)} node={child} pathname={pathname} />
          ))}
        </ul>
      </details>
    </li>
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
