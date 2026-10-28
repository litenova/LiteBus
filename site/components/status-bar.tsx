import Link from 'next/link';
import type { ReactNode } from 'react';
import { Logo } from '@/components/logo';

/**
 * Bottom status bar shared by every page.
 *
 * Renders the mock status row: the home segment (logo plus "LiteBus", linking to `/`) first,
 * then `children` in the left group and `end` in the right group. Children are `span` or `a`
 * segments; the home segment stays neutral with a right border, never an accent block.
 */
export function StatusBar({ children, end }: Readonly<{ children?: ReactNode; end?: ReactNode }>) {
  return (
    <div className="status">
      <div>
        <Link className="home" href="/">
          <Logo size={13} />
          LiteBus
        </Link>
        {children}
      </div>
      <div>{end}</div>
    </div>
  );
}
