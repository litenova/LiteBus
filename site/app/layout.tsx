import type { Metadata } from 'next';
import { JetBrains_Mono, Source_Sans_3 } from 'next/font/google';
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

/**
 * The text face for the whole site: a humanist sans designed for running text, narrower than a UI face, so justified
 * paragraphs keep even word spacing.
 */
const sourceSans = Source_Sans_3({
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
 * Theme bootstrap: runs before first paint and applies the stored scheme override.
 *
 * The server renders no `data-theme`, so the page follows the system scheme through CSS. This
 * script pins `data-theme`, `<meta name="color-scheme">`, and the theme-color metas only when
 * the visitor chose light or dark before; otherwise it leaves the system default in place and
 * restores the media-paired theme colors. It reads the stored value from
 * `localStorage['litebus-theme']`, where the header toggle keeps `light` or `dark` and removes
 * the entry to return to the system scheme.
 */
const themeScript = `try{var t=localStorage.getItem('litebus-theme');var m=document.querySelector('meta[name="color-scheme"]');var sysDark=window.matchMedia('(prefers-color-scheme: dark)').matches;var light='#f8f8f8';var dark='#16181d';if(t==='light'||t==='dark'){document.documentElement.setAttribute('data-theme',t);if(m){m.setAttribute('content',t)}}else{document.documentElement.removeAttribute('data-theme');if(m){m.setAttribute('content','light dark')}}var tc=document.querySelectorAll('meta[name="theme-color"]');for(var i=0;i<tc.length;i++){var el=tc[i];if(t==='light'){el.setAttribute('content',light)}else if(t==='dark'){el.setAttribute('content',dark)}else{var media=el.getAttribute('media')||'';if(media.indexOf('light')!==-1){el.setAttribute('content',light)}else if(media.indexOf('dark')!==-1){el.setAttribute('content',dark)}else{el.setAttribute('content',sysDark?dark:light)}}}}catch(e){}`;

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

/**
 * Root layout for every page.
 *
 * Renders `<html>` without a theme override so the page follows the system scheme, with the
 * Source Sans 3 and JetBrains Mono font variables. The `<head>` declares both schemes, pairs a
 * theme color with each scheme media query, and runs the theme bootstrap script immediately
 * after so the stored override, the color-scheme meta, and the theme colors match from the
 * first paint. Pages compose the window themselves from `SiteHeader`, a `.grow` element,
 * `SiteFooter`, and `StatusBar`.
 */
export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en" suppressHydrationWarning className={`${sourceSans.variable} ${jetBrainsMono.variable}`}>
      <head>
        <meta name="color-scheme" content="light dark" />
        <meta name="theme-color" media="(prefers-color-scheme: light)" content="#f8f8f8" />
        <meta name="theme-color" media="(prefers-color-scheme: dark)" content="#16181d" />
        <script dangerouslySetInnerHTML={{ __html: themeScript }} />
      </head>
      <body>{children}</body>
    </html>
  );
}
