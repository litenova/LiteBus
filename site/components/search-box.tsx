'use client';

import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useEffect, useId, useRef, useState, useSyncExternalStore } from 'react';

/**
 * The platform never changes during a page view, so there is nothing to subscribe to.
 */
function subscribeToNothing(): () => void {
  return () => {};
}

/**
 * Whether the visitor is on macOS, where the shortcut is Cmd K.
 */
function readIsMac(): boolean {
  return window.navigator.platform.toLowerCase().includes('mac');
}

/**
 * The server renders the Ctrl K hint.
 */
function readIsMacOnServer(): boolean {
  return false;
}
import type { SortedResult } from 'fumadocs-core/search';

/**
 * How long to wait after the last keystroke before asking the search index.
 */
const debounceMs = 150;

/**
 * Queries shorter than this are prefixes of everything and answer nothing.
 */
const minQueryLength = 2;

/**
 * How many matches the dropdown shows at most.
 */
const maxResults = 8;

/**
 * Searches the documentation.
 *
 * Typing past the minimum length queries the index; arrow keys move through the matches, Enter opens the active one,
 * and Escape clears the box. Ctrl K or Cmd K focuses the box from anywhere on the page.
 */
export function SearchBox() {
  const router = useRouter();
  const [query, setQuery] = useState('');
  const [found, setResults] = useState<readonly SortedResult[]>([]);
  const [active, setActive] = useState(0);
  const isMac = useSyncExternalStore(subscribeToNothing, readIsMac, readIsMacOnServer);
  const inputRef = useRef<HTMLInputElement>(null);
  const listboxId = useId();

  // A query below the minimum length shows nothing, whatever an earlier, longer query returned.
  const results = query.trim().length < minQueryLength ? [] : found;
  const expanded = results.length > 0;
  const activeOptionId =
    results.length === 0 ? undefined : `${listboxId}-option-${Math.min(active, results.length - 1)}`;


  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
        const target = event.target;

        // Typing in any other editable field keeps the browser binding; only this input refocuses.
        if (
          target instanceof HTMLElement &&
          target !== inputRef.current &&
          target.closest('input, textarea, select, [contenteditable]') !== null
        ) {
          return;
        }

        event.preventDefault();
        inputRef.current?.focus();
      }
    };

    window.addEventListener('keydown', onKeyDown);

    return () => {
      window.removeEventListener('keydown', onKeyDown);
    };
  }, []);

  useEffect(() => {
    const trimmed = query.trim();

    if (trimmed.length < minQueryLength) {
      return;
    }

    let cancelled = false;
    const timer = window.setTimeout(() => {
      const params = new URLSearchParams({ query: trimmed });

      void fetch(`/api/search?${params.toString()}`)
        .then(async (response) => (response.ok ? ((await response.json()) as SortedResult[]) : []))
        .then((found) => {
          if (!cancelled) {
            setResults(found.slice(0, maxResults));
            setActive(0);
          }
        })
        .catch(() => {
          if (!cancelled) {
            setResults([]);
          }
        });
    }, debounceMs);

    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, [query]);

  return (
    <div className="search">
      <input
        ref={inputRef}
        type="search"
        value={query}
        onChange={(event) => {
          setQuery(event.target.value);
        }}
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            setQuery('');
            setResults([]);
          } else if (event.key === 'ArrowDown' && results.length > 0) {
            event.preventDefault();
            setActive((index) => (index + 1) % results.length);
          } else if (event.key === 'ArrowUp' && results.length > 0) {
            event.preventDefault();
            setActive((index) => (index - 1 + results.length) % results.length);
          } else if (event.key === 'Enter' && results.length > 0) {
            const target = results[Math.min(active, results.length - 1)];

            setQuery('');
            setResults([]);
            router.push(target.url);
          }
        }}
        placeholder="Search docs"
        aria-label="Search documentation"
        autoComplete="off"
        spellCheck={false}
        role="combobox"
        aria-expanded={expanded}
        aria-controls={expanded ? listboxId : undefined}
        aria-activedescendant={activeOptionId}
        aria-autocomplete="list"
      />
      <kbd>{isMac ? 'Cmd K' : 'Ctrl K'}</kbd>
      {results.length > 0 ? (
        <ul className="search-results" role="listbox" id={listboxId} aria-label="Search results">
          {results.map((result, index) => (
            <li
              key={result.id}
              role="option"
              id={`${listboxId}-option-${index}`}
              aria-selected={index === Math.min(active, results.length - 1)}
            >
              <Link
                href={result.url}
                data-active={index === active}
                tabIndex={-1}
                onClick={() => {
                  setQuery('');
                  setResults([]);
                }}
              >
                {result.type === 'page' ? <b>{result.content}</b> : <span>{result.content}</span>}
              </Link>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}
