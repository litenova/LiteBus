'use client';

import { useRouter, usePathname } from 'next/navigation';
import { useId } from 'react';
import { docsVersions, findVersion, slugFromPathname, versionHref } from '@/lib/versions';

/**
 * Moves the reader between documentation versions.
 *
 * Switching keeps the page the reader is on rather than returning them to the index, because the same page usually
 * exists in both lines and losing your place to compare two versions of one page defeats the point of the control. A
 * page that only exists in one version lands on the documentation not-found boundary, which offers the versions that
 * do have it.
 */
export function VersionSwitcher({ current }: Readonly<{ current: string }>) {
  const router = useRouter();
  const pathname = usePathname();
  const labelId = useId();
  const selected = findVersion(current) ?? docsVersions[0];

  return (
    <div className="litebus-version-switcher">
      <label className="litebus-version-switcher-label" id={labelId} htmlFor={`${labelId}-select`}>
        Version
      </label>
      <select
        id={`${labelId}-select`}
        className="litebus-version-switcher-select"
        value={selected.id}
        onChange={(event) => {
          const target = findVersion(event.target.value);

          if (target !== undefined && target.id !== selected.id) {
            router.push(versionHref(target, slugFromPathname(pathname)));
          }
        }}
      >
        {docsVersions.map((version) => (
          <option key={version.id} value={version.id}>
            {version.label} - {version.description}
          </option>
        ))}
      </select>
      <span className="litebus-version-switcher-release">
        Documents {selected.release}
        {selected.status === 'prerelease' ? ' (pre-release)' : ''}
      </span>
    </div>
  );
}
