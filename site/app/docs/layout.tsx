import { DocsShell } from '@/components/docs-shell';
import { defaultVersion } from '@/lib/versions';

// The default version is served unprefixed so the canonical documentation URL does not move when a release is cut.
export default function DocsRootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <DocsShell version={defaultVersion}>{children}</DocsShell>;
}
