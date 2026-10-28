'use client';

import { type ReactNode, useEffect, useState } from 'react';

/**
 * One outline entry: the section link and title.
 */
export type DocsOutlineEntry = Readonly<{
  /** Fragment link to the section, for example `#register-litebus`. */
  url: string;
  /** Section title. */
  title: ReactNode;
}>;

/**
 * How far below the viewport top a heading counts as passed.
 *
 * The sticky header is 52px and the mobile menu row adds 48px, so 120px keeps the current entry on the section the
 * reader is actually reading.
 */
const passedOffsetPx = 120;

/**
 * The fragment part of an outline URL, for matching against observed heading ids.
 */
function hashFor(url: string): string {
  const hashIndex = url.indexOf('#');

  return hashIndex === -1 ? url : url.slice(hashIndex);
}

/**
 * The documentation outline with scroll-spy highlighting.
 *
 * Renders one link per level-two section. An IntersectionObserver watches the page's `h2` elements and the entry for
 * the heading nearest the top is current, marked with `cur` and `aria-current`. No entry is current before hydration
 * or while the reader is above the first section.
 */
export function DocsOutline({ headings }: Readonly<{ headings: readonly DocsOutlineEntry[] }>) {
  const [current, setCurrent] = useState<string | null>(null);

  useEffect(() => {
    /** Marks the last heading at or above the passed offset as current. */
    function updateCurrent(): void {
      const sections = Array.from(document.querySelectorAll('#main h2[id]'));
      let found: string | null = null;

      for (const section of sections) {
        if (section.getBoundingClientRect().top <= passedOffsetPx) {
          found = `#${section.id}`;
        } else {
          break;
        }
      }

      setCurrent(found);
    }

    updateCurrent();

    const observer = new IntersectionObserver(updateCurrent, { rootMargin: '-80px 0px -80% 0px' });
    const sections = document.querySelectorAll('#main h2[id]');

    sections.forEach((section) => {
      observer.observe(section);
    });

    return () => {
      observer.disconnect();
    };
  }, [headings]);

  return (
    <>
      {headings.map((entry) => {
        const isCurrent = hashFor(entry.url) === current;

        return (
          <a
            key={entry.url}
            href={entry.url}
            className={isCurrent ? 'cur' : undefined}
            aria-current={isCurrent ? 'location' : undefined}
          >
            {entry.title}
          </a>
        );
      })}
    </>
  );
}
