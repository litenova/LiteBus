'use client';

import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useEffect, useState } from 'react';
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
 * Searches one documentation version.
 *
 * Every request carries that version's id as `tag`, so a question asked from one version's pages is answered from
 * that version's pages. Typing past the minimum length queries the index; arrow keys move through the matches, Enter
 * opens the active one, and Escape clears the box.
 */
export function SearchBox({ versionId }: Readonly<{ versionId: string }>) {
  const router = useRouter();
  const [query, setQuery] = useState('');
  const [found, setResults] = useState<readonly SortedResult[]>([]);
  const [active, setActive] = useState(0);

  // A query below the minimum length shows nothing, whatever an earlier, longer query returned.
  const results = query.trim().length < minQueryLength ? [] : found;

  useEffect(() => {
    const trimmed = query.trim();

    if (trimmed.length < minQueryLength) {
      return;
    }

    let cancelled = false;
    const timer = window.setTimeout(() => {
      const params = new URLSearchParams({ query: trimmed, tag: versionId });

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
  }, [query, versionId]);

  return (
    <div className="search">
      <input
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
      />
      {results.length > 0 ? (
        <ul className="search-results">
          {results.map((result, index) => (
            <li key={result.id}>
              <Link
                href={result.url}
                data-active={index === active}
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
