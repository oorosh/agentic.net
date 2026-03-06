import {themes as prismThemes} from 'prism-react-renderer';
import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

const config: Config = {
  title: 'Agentic.NET',
  tagline: 'Build AI agents in .NET — CLI tools, chat bots, and autonomous assistants.',
  favicon: 'img/favicon.ico',

  future: {
    v4: true,
  },

  url: 'https://oorosh.github.io',
  baseUrl: '/agentic.net/',

  organizationName: 'oorosh',
  projectName: 'agentic.net',
  trailingSlash: false,

  onBrokenLinks: 'throw',
  markdown: {
    hooks: {
      onBrokenMarkdownLinks: 'warn',
    },
  },

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  presets: [
    [
      'classic',
      {
        docs: {
          sidebarPath: './sidebars.ts',
          editUrl: 'https://github.com/oorosh/agentic.net/tree/main/website/',
        },
        blog: {
          showReadingTime: true,
          blogSidebarTitle: 'All articles',
          blogSidebarCount: 'ALL',
          feedOptions: {
            type: ['rss', 'atom'],
            xslt: true,
          },
          editUrl: 'https://github.com/oorosh/agentic.net/tree/main/website/',
          onInlineTags: 'warn',
          onInlineAuthors: 'warn',
          onUntruncatedBlogPosts: 'warn',
        },
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],

  themeConfig: {
    image: 'img/social-card.png',
    colorMode: {
      respectPrefersColorScheme: true,
    },
    navbar: {
      title: 'Agentic.NET',
      logo: {
        alt: 'Agentic.NET Logo',
        src: 'img/logo.png',
      },
      items: [
        {
          type: 'docSidebar',
          sidebarId: 'docsSidebar',
          position: 'left',
          label: 'Docs',
        },
        {to: '/blog', label: 'Articles', position: 'left'},
        {
          href: 'https://www.nuget.org/packages/Agentic.NET',
          label: 'NuGet',
          position: 'right',
        },
        {
          href: 'https://github.com/oorosh/agentic.net',
          label: 'GitHub',
          position: 'right',
        },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Learn',
          items: [
            {label: 'Quick Start', to: '/docs/quick-start'},
            {label: 'User Manual', to: '/docs/user-manual'},
            {label: 'Articles', to: '/blog'},
          ],
        },
        {
          title: 'Project',
          items: [
            {label: 'GitHub', href: 'https://github.com/oorosh/agentic.net'},
            {label: 'NuGet', href: 'https://www.nuget.org/packages/Agentic.NET'},
            {label: 'Changelog', href: 'https://github.com/oorosh/agentic.net/blob/main/CHANGELOG.md'},
          ],
        },
      ],
      copyright: `Copyright © ${new Date().getFullYear()} Agentic.NET. Built with Docusaurus.`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
      additionalLanguages: ['csharp', 'bash', 'json'],
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
