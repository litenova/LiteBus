import { DocsShell } from '@/components/docs-shell';
import './docs.css';

export default function DocsRootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <DocsShell>{children}</DocsShell>;
}
