'use client';

import { useSyncExternalStore } from 'react';

/**
 * The stored scheme override: `light` or `dark` pins that scheme, `null` follows the system.
 */
type StoredTheme = 'light' | 'dark' | null;

/**
 * The theme-color values paired with the light and dark schemes.
 */
const lightThemeColor = '#f8f8f8';
const darkThemeColor = '#16181d';

/**
 * Reads the stored override from the document: the `data-theme` attribute mirrors
 * `localStorage['litebus-theme']`, and its absence means the system scheme.
 */
function readStored(): StoredTheme {
  const value = document.documentElement.getAttribute('data-theme');

  return value === 'light' || value === 'dark' ? value : null;
}

/**
 * Whether the system scheme is dark right now.
 */
function readSystemDark(): boolean {
  return window.matchMedia('(prefers-color-scheme: dark)').matches;
}

/**
 * Re-renders subscribers when the stored override or the system scheme changes.
 */
function subscribe(onChange: () => void): () => void {
  const observer = new MutationObserver(onChange);
  observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });

  const media = window.matchMedia('(prefers-color-scheme: dark)');
  media.addEventListener('change', onChange);

  const onStorage = (event: StorageEvent) => {
    if (event.key === 'litebus-theme') {
      onChange();
    }
  };
  window.addEventListener('storage', onStorage);

  return () => {
    observer.disconnect();
    media.removeEventListener('change', onChange);
    window.removeEventListener('storage', onStorage);
  };
}

/**
 * The scheme the document shows now: the stored override, or the system scheme.
 */
function currentTheme(): 'dark' | 'light' {
  const stored = readStored();

  if (stored !== null) {
    return stored;
  }

  return readSystemDark() ? 'dark' : 'light';
}

/**
 * The server render assumes dark; the client corrects to the stored or system scheme on hydration.
 */
function serverTheme(): 'dark' {
  return 'dark';
}

/**
 * Applies a stored override (or the system default) to the document.
 *
 * Sets `data-theme` (removed for the system scheme), `<meta name="color-scheme">` (`light dark`
 * for the system scheme), and every theme-color meta so the browser chrome matches the resolved
 * scheme. For the system scheme the media-paired theme colors are restored.
 */
function applyTheme(stored: StoredTheme, systemDark: boolean): void {
  if (stored === null) {
    document.documentElement.removeAttribute('data-theme');
  } else {
    document.documentElement.setAttribute('data-theme', stored);
  }

  document
    .querySelector('meta[name="color-scheme"]')
    ?.setAttribute('content', stored ?? 'light dark');

  for (const meta of document.querySelectorAll('meta[name="theme-color"]')) {
    if (stored === 'light') {
      meta.setAttribute('content', lightThemeColor);
    } else if (stored === 'dark') {
      meta.setAttribute('content', darkThemeColor);
    } else {
      const media = meta.getAttribute('media') ?? '';

      if (media.includes('light')) {
        meta.setAttribute('content', lightThemeColor);
      } else if (media.includes('dark')) {
        meta.setAttribute('content', darkThemeColor);
      } else {
        meta.setAttribute('content', systemDark ? darkThemeColor : lightThemeColor);
      }
    }
  }

  try {
    if (stored === null) {
      window.localStorage.removeItem('litebus-theme');
    } else {
      window.localStorage.setItem('litebus-theme', stored);
    }
  } catch {
    // Storage unavailable (private mode, blocked cookies): the theme still switches for this page view.
  }
}

/**
 * Theme toggle button from the mock top bar.
 *
 * The site follows the system scheme by default; this toggle pins the opposite of the rendered
 * scheme as an explicit override. Toggling back toward the system scheme removes the stored
 * override so future system changes apply again. Renders both the sun and the moon; CSS shows
 * the sun in dark mode and the moon in light mode, so the server render is already right before
 * hydration.
 */
export function ThemeToggle() {
  const theme = useSyncExternalStore(subscribe, currentTheme, serverTheme);
  const label = theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme';

  return (
    <button
      type="button"
      className="tt"
      title={label}
      aria-label={label}
      aria-pressed={theme === 'light'}
      onClick={() => {
        const systemDark = readSystemDark();
        const system = systemDark ? 'dark' : 'light';
        const target = theme === 'dark' ? 'light' : 'dark';

        // Toggling toward the system scheme returns to it instead of pinning the same scheme.
        applyTheme(target === system ? null : target, systemDark);
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
