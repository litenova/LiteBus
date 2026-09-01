import versionsFile from '@/versions.json';

/**
 * Documentation versions.
 *
 * The declarations live in `site/versions.json` so that the build gates under `site/scripts` can read the same list
 * from plain Node. See that file for the on-disk layout and for what to change when a release is cut.
 */

export type VersionStatus = 'stable' | 'prerelease';

export type DocsVersion = {
  /** URL and filesystem identifier, and the tag the search dialog sends to pick this version's index. */
  readonly id: string;
  /** Short label shown in the version switcher. */
  readonly label: string;
  /** One line describing what this version is, shown beside the label. */
  readonly description: string;
  /** Content directory, relative to the site root. */
  readonly dir: string;
  /** The package version this documentation describes. */
  readonly release: string;
  readonly status: VersionStatus;
  /** Git ref the sources live on, used to build fallback links into the repository. */
  readonly ref: string;
  readonly default: boolean;
};

function assertStatus(value: string, id: string): VersionStatus {
  if (value !== 'stable' && value !== 'prerelease') {
    throw new Error(`Version '${id}' declares an unknown status '${value}' in versions.json.`);
  }

  return value;
}

export const docsVersions: readonly DocsVersion[] = versionsFile.versions.map((version) => ({
  ...version,
  status: assertStatus(version.status, version.id),
}));

const declaredDefaults = docsVersions.filter((version) => version.default);

if (declaredDefaults.length !== 1) {
  throw new Error(
    `versions.json must declare exactly one default version, but ${declaredDefaults.length} are marked default.`,
  );
}

export const defaultVersion: DocsVersion = declaredDefaults[0];

/** Versions that are not the default, in declaration order. */
export const prefixedVersions: readonly DocsVersion[] = docsVersions.filter(
  (version) => !version.default,
);

/**
 * Where a version's documentation is rooted.
 *
 * The default version is unprefixed so the canonical documentation URL does not move when a release is cut. Every
 * other version carries its identifier as the first path segment.
 */
export function versionBasePath(version: DocsVersion): string {
  return version.default ? '/docs' : `/${version.id}/docs`;
}

export function findVersion(id: string | undefined): DocsVersion | undefined {
  return id === undefined ? undefined : docsVersions.find((version) => version.id === id);
}

/** The version a rendered path belongs to, falling back to the default for unprefixed paths. */
export function versionFromPathname(pathname: string): DocsVersion {
  const [, first] = pathname.split('/');

  return prefixedVersions.find((version) => version.id === first) ?? defaultVersion;
}

/** The slug segments of a documentation path, with any version prefix and the `docs` segment removed. */
export function slugFromPathname(pathname: string): string[] {
  const segments = pathname.split('/').filter((segment) => segment.length > 0);
  const withoutVersion = prefixedVersions.some((version) => version.id === segments[0])
    ? segments.slice(1)
    : segments;

  return withoutVersion[0] === 'docs' ? withoutVersion.slice(1) : withoutVersion;
}

/** The same documentation page in another version. */
export function versionHref(version: DocsVersion, slug: readonly string[]): string {
  return [versionBasePath(version), ...slug].join('/');
}
