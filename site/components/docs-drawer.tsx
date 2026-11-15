'use client';

import { useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { SearchBox } from '@/components/search-box';
import { changelogUrl, latestRelease } from '@/lib/site';

/**
 * The documentation drawer: the mobile menu row, the backdrop, and the explorer.
 *
 * Below 860px the explorer leaves the page grid and becomes a drawer that slides in from the left. The menu button
 * opens it; the backdrop, the `x` button in the drawer header, and `Escape` close it, and navigating to a page
 * closes it because the sidebar remounts the drawer for the new path. Focus moves to the close button on open and
 * back to the menu button on close, Tab stays inside the drawer while it is open, and the header, article, and
 * status bar are inert behind it. While closed on mobile the explorer is inert and hidden so its links are neither
 * focusable nor announced. The page behind does not scroll while the drawer is open. At wider viewports the menu
 * row and backdrop stay hidden and the explorer renders inline with the header search; the drawer search shows only
 * below 860px, above the navigation groups.
 */
export function DocsDrawer({
  parent,
  current,
  navigation,
}: Readonly<{ parent: string | undefined; current: string; navigation: ReactNode }>) {
  const [open, setOpen] = useState(false);
  const [isMobile, setIsMobile] = useState(false);
  const menuButtonRef = useRef<HTMLButtonElement>(null);
  const navRef = useRef<HTMLElement>(null);
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const wasOpenRef = useRef(false);
  const changelogLabel = `Changelog v${latestRelease}`;

  // Tracks the mobile layout so the closed explorer is inert only where it acts as a drawer.
  useEffect(() => {
    const media = window.matchMedia('(max-width: 860px)');

    const update = () => {
      setIsMobile(media.matches);
    };

    update();
    media.addEventListener('change', update);

    return () => {
      media.removeEventListener('change', update);
    };
  }, []);

  // While the drawer is open, lock the page scroll, close on Escape, trap Tab inside the drawer, hide the page
  // behind it from assistive technology, and move focus to the close button. When it closes again, focus returns
  // to the menu button.
  useEffect(() => {
    if (!open) {
      if (wasOpenRef.current) {
        wasOpenRef.current = false;
        menuButtonRef.current?.focus({ preventScroll: true });
      }

      return;
    }

    wasOpenRef.current = true;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    closeButtonRef.current?.focus({ preventScroll: true });

    const inertTargets = [
      document.querySelector('header'),
      document.getElementById('main'),
      document.querySelector('.status'),
    ];

    for (const target of inertTargets) {
      target?.setAttribute('inert', '');
    }

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setOpen(false);

        return;
      }

      if (event.key !== 'Tab' || navRef.current === null) {
        return;
      }

      const focusable = navRef.current.querySelectorAll<HTMLElement>(
        'a[href], button:not([disabled]), input:not([disabled]), [tabindex]:not([tabindex="-1"])',
      );

      if (focusable.length === 0) {
        event.preventDefault();

        return;
      }

      const items = Array.from(focusable);
      const activeIndex = document.activeElement instanceof HTMLElement ? items.indexOf(document.activeElement) : -1;
      const first = items[0];
      const last = items[items.length - 1];

      if (event.shiftKey && activeIndex <= 0) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && (activeIndex === -1 || activeIndex === items.length - 1)) {
        event.preventDefault();
        first.focus();
      }
    };

    window.addEventListener('keydown', onKeyDown);

    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', onKeyDown);

      for (const target of inertTargets) {
        target?.removeAttribute('inert');
      }
    };
  }, [open ]);

  const modal = open && isMobile;

  return (
    <>
      <div className="menurow">
        <button
          ref={menuButtonRef}
          type="button"
          className="menubtn"
          aria-expanded={open}
          aria-controls="docs-nav"
          onClick={() => {
            setOpen((value) => !value);
          }}
        >
          <svg className="burger" viewBox="0 0 16 16" aria-hidden="true">
            <path d="M2 4h12M2 8h12M2 12h12" />
          </svg>
          Menu
        </button>
        <span className="path">
          {parent === undefined ? null : (
            <span className="parent">
              {parent} <b aria-hidden="true">&gt;</b>{' '}
            </span>
          )}
          {current}
        </span>
        <a className="ver" href={changelogUrl} aria-label={changelogLabel}>
          v{latestRelease}
        </a>
      </div>
      <button
        type="button"
        className={open ? 'scrim on' : 'scrim'}
        aria-hidden="true"
        tabIndex={-1}
        onClick={() => {
          setOpen(false);
        }}
      />
      <nav
        ref={navRef}
        id="docs-nav"
        aria-label="Documentation"
        role={modal ? 'dialog' : undefined}
        aria-modal={modal ? true : undefined}
        inert={!open && isMobile}
        className={open ? 'explorer open' : 'explorer'}
        tabIndex={-1}
      >
        <div className="xws">
          <b>Docs</b>
          <a className="ver" href={changelogUrl} aria-label={changelogLabel}>
            v{latestRelease}
          </a>
          <button
            ref={closeButtonRef}
            type="button"
            className="xclose"
            aria-label="Close menu"
            onClick={() => {
              setOpen(false);
            }}
          >
            x
          </button>
        </div>
        <SearchBox />
        {navigation}
      </nav>
    </>
  );
}
