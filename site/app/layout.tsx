import type { Metadata, Viewport } from 'next';
import { Inter, JetBrains_Mono } from 'next/font/google';
import './globals.css';
import {
  authorName,
  authorUrl,
  siteDescription,
  siteKeywords,
  siteName,
  siteOgImage,
  siteTitle,
  siteUrl,
} from '@/lib/site';

const inter = Inter({
  subsets: ['latin'],
  variable: '--font-sans',
  display: 'swap',
  weight: ['400', '500', '600', '700'],
});

const jetBrainsMono = JetBrains_Mono({
  subsets: ['latin'],
  variable: '--font-mono',
  display: 'swap',
  weight: ['400', '500', '600', '700'],
});

/**
 * Theme bootstrap: runs before first paint and restores the stored light theme.
 *
 * The server always renders `data-theme="dark"`; this script switches to light only when the
 * visitor chose light before. The site never reads `prefers-color-scheme`.
 */
const themeScript = `try{if(localStorage.getItem('litebus-theme')==='light'){document.documentElement.setAttribute('data-theme','light');var m=document.querySelector('meta[name="theme-color"]');if(m){m.setAttribute('content','#f6f7f9')}}}catch(e){}`;

export const metadata: Metadata = {
  metadataBase: new URL(siteUrl),
  title: {
    default: siteTitle,
    template: `%s | ${siteName}`,
  },
  description: siteDescription,
  applicationName: siteName,
  keywords: siteKeywords,
  authors: [{ name: authorName, url: authorUrl }],
  creator: authorName,
  publisher: authorName,
  category: 'technology',
  alternates: {
    canonical: '/',
  },
  openGraph: {
    type: 'website',
    url: siteUrl,
    siteName,
    title: siteTitle,
    description: siteDescription,
    locale: 'en_US',
    images: [
      {
        url: siteOgImage,
        width: 1280,
        height: 640,
        alt: `${siteName} - ${siteDescription}`,
      },
    ],
  },
  twitter: {
    card: 'summary_large_image',
    title: siteTitle,
    description: siteDescription,
    images: [siteOgImage],
  },
  robots: {
    index: true,
    follow: true,
    googleBot: {
      index: true,
      follow: true,
      'max-image-preview': 'large',
      'max-snippet': -1,
      'max-video-preview': -1,
    },
  },
  icons: {
    icon: [{ url: '/icon.svg', type: 'image/svg+xml' }],
    shortcut: ['/icon.svg'],
    apple: [{ url: '/icon.png', sizes: '128x128', type: 'image/png' }],
  },
  manifest: '/manifest.webmanifest',
};

export const viewport: Viewport = {
  colorScheme: 'dark light',
  themeColor: '#16181d',
};

/**
 * Root layout for every page.
 *
 * Renders `<html>` dark by default with the Inter and JetBrains Mono font variables, plus the
 * theme bootstrap script in `<head>`. Pages compose the window themselves from `SiteHeader`, a
 * `.grow` element, `SiteFooter`, and `StatusBar`.
 */
export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html
      lang="en"
      data-theme="dark"
      suppressHydrationWarning
      className={`${inter.variable} ${jetBrainsMono.variable}`}
    >
      <head>
        <script dangerouslySetInnerHTML={{ __html: themeScript }} />
      </head>
      <body>{children}</body>
    </html>
  );
}
