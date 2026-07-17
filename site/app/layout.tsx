import type { Metadata } from 'next';
import './globals.css';

export const metadata: Metadata = {
  title: {
    default: 'LiteBus documentation',
    template: '%s | LiteBus',
  },
  description: 'Mediator and durable messaging building blocks for .NET.',
  icons: {
    icon: '/logo.svg',
  },
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
