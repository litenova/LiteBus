'use client';

import { useSyncExternalStore } from 'react';

/**
 * Re-renders subscribers when the `data-theme` attribute on `<html>` changes.
 */
function subscribe(onChange: () => void): () => void {
  const observer = new MutationObserver(onChange);
  observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });

  return () => observer.disconnect();
}

/**
 * The theme the document shows now.
 */
function currentTheme(): 'dark' | 'light' {
  return document.documentElement.getAttribute('data-theme') === 'light' ? 'light' : 'dark';
}

/**
 * The server renders dark, matching the attribute it sends.
 */
function serverTheme(): 'dark' {
  return 'dark';
}

/**
 * Theme toggle button from the mock top bar.
 *
 * Renders both the sun and the moon; CSS shows the sun in dark mode and the moon in light mode,
 * so the server render is already right before hydration. Clicking flips `data-theme` on
 * `<html>` and remembers the choice in `localStorage['litebus-theme']`.
 */
export function ThemeToggle() {
  const theme = useSyncExternalStore(subscribe, currentTheme, serverTheme);

  const next = theme === 'dark' ? 'light' : 'dark';

  return (
    <button
      type="button"
      className="tt"
      title="Switch theme"
      aria-label={theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}
      aria-pressed={theme === 'light'}
      onClick={() => {
        document.documentElement.setAttribute('data-theme', next);
        document.querySelector('meta[name="theme-color"]')?.setAttribute('content', next === 'light' ? '#f6f7f9' : '#16181d');

        try {
          window.localStorage.setItem('litebus-theme', next);
        } catch {
          // Storage unavailable (private mode, blocked cookies): the theme still switches for this page view.
        }
      }}
    >
      <svg className="sun" viewBox="0 0 16 16" aria-hidden="true">
        <circle cx="8" cy="8" r="3" />
        <path d="M8 1v1.5M8 13.5V15M1 8h1.5M13.5 8H15M3.05 3.05l1.06 1.06M11.89 11.89l1.06 1.06M3.05 12.95l1.06-1.06M11.89 4.11l1.06-1.06" />
      </svg>
      <svg className="moon" viewBox="0 0 16 16" aria-hidden="true">
        <path d="M13.5 9.5A5.5 5.5 0 0 1 6.5 2.5a5.5 5.5 0 1 0 7 7Z" />
      </svg>
    </button>
  );
}
