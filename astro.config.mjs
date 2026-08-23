// @ts-check
import { defineConfig } from 'astro/config';
import { unified } from '@astrojs/markdown-remark';
import starlight from '@astrojs/starlight';
import mermaid from 'astro-mermaid';
import { rehypeMarkdownLinks } from './plugins/rehype-markdown-links.mjs';
import { remarkGithubAlerts } from './plugins/remark-github-alerts.mjs';

// ---- 対象リポジトリに合わせて書き換える ----
// user.github.io/repo で公開      → site: 'https://user.github.io', BASE: '/repo'
// user.github.io（専用リポジトリ） → site: 'https://user.github.io', BASE: '/'
// 独自ドメイン                     → site: 'https://example.com',    BASE: '/'
const SITE = 'https://microsoft.github.io';
const BASE = '/aspnetcore-ja-handbook';
// ------------------------------------------

export default defineConfig({
	site: SITE,
	base: BASE,
	markdown: {
		// Astro v7 以降の形式。markdown.rehypePlugins を直接書くのは非推奨。
		processor: unified({
			// GitHub のアラート記法（> [!NOTE] など）を Starlight の aside に変換する。
			// 未導入だと [!NOTE] という文字列が本文に出たままになる。
			// md にアラート記法が 1 件も無い場合は、この import と登録ごと削除してよい。
			remarkPlugins: [remarkGithubAlerts],
			rehypePlugins: [[rehypeMarkdownLinks, { base: BASE }]],
		}),
	},
	integrations: [
		// Mermaid のコードブロックを図として描画する。
		// Starlight 単体では Mermaid に対応しておらず、
		// 未導入だと flowchart TD / gantt などが生テキストのまま表示される。
		//
		// 注意: 必ず starlight() より前に置くこと。
		// 後ろに置くと Starlight のコードブロック処理が先に走り、図にならない。
		//
		// md に mermaid ブロックが 1 件も無い場合は、この import と
		// integration ごと削除してよい（依存も不要になる）。
		mermaid({
			theme: 'default',
			// Starlight のダーク/ライト切替（data-theme 属性）に追従させる。
			autoTheme: true,
			enableLog: false,
		}),
		starlight({
			title: 'ASP.NET Core ハンドブック',
			defaultLocale: 'root',
			locales: {
				root: { label: '日本語', lang: 'ja' },
			},
			// sidebar は未設定にしておく。
			// 未設定なら src/content/docs/ 配下から自動生成され、
			// 並び順は slug（フォルダ名）順になる。
			// → 01-, 02- ... の連番プレフィックスがそのまま並び順になる。
			//
			// 注意1: frontmatter の title は並び順に一切影響しない。
			// 注意2: サブディレクトリは必ず折りたたみグループになり、
			//        グループ名はフォルダ名の生文字列になる。
			//        これを index.md の title に置き換えるのが
			//        下の components.Sidebar（Sidebar.astro）の役割。
			//
			// 明示的にグループ分けしたい場合のみ、以下のように書く。
			// 存在しない directory を指定してもエラーにならず、
			// その項目が黙って消えるだけなので必ず実在するパスを指定すること。
			// sidebar: [
			// 	{ label: 'はじめに', slug: 'index' },
			// 	// Starlight v0.39 以降の書式。
			// 	// 旧: { label: 'x', autogenerate: {...} } はエラーになる。
			// 	{ label: 'ガイド', items: [{ autogenerate: { directory: 'guides' } }] },
			// ],
			tableOfContents: { minHeadingLevel: 2, maxHeadingLevel: 3 },
			pagination: true,
			// Sidebar: 折りたたみグループの中身をページの見出しにする。
			//   - グループ名は index.md の frontmatter title
			//   - グループ名クリックで index.md を開く
			//   - 子項目は index.md の `##` 見出し（目次は除外）
			//   不要なら components.Sidebar の行と src/components/Sidebar.astro,
			//   src/components/SidebarSublist.astro, src/lib/page-headings.ts を削除する。
			//
			// SocialIcons: 左サイドバー／右サイドバー（目次）の開閉ボタン。
			//   ヘッダー右側にボタンを追加する
			//   （PanelToggles.astro は元の SocialIcons もそのまま描画する）。
			//   不要なら SocialIcons の行と customCss / head を削除する。
			components: {
				Sidebar: './src/components/Sidebar.astro',
				SocialIcons: './src/components/PanelToggles.astro',
			},
			customCss: [
				'./src/styles/panel-toggle.css',
				// 狭い画面で表がはみ出して横スクロールになるのを防ぐ。
				'./src/styles/table-mobile.css',
			],
			head: [
				{
					// 描画前に折りたたみ状態を復元して、
					// サイドバーが一瞬見えてから消える（FOUC）のを防ぐ。
					// バンドルされる <script> では描画に間に合わないため、
					// ここでインラインとして head に入れる必要がある。
					tag: 'script',
					content: [
						'try {',
						'  var d = document.documentElement;',
						"  if (localStorage.getItem('sl-sidebar-collapsed') === '1') d.setAttribute('data-sidebar-collapsed', '');",
						"  if (localStorage.getItem('sl-toc-collapsed') === '1') d.setAttribute('data-toc-collapsed', '');",
						'} catch (e) {}',
					].join('\n'),
				},
			],
			// GitHub のソースへのリンクを出す場合
			// editLink: { baseUrl: 'https://github.com/<owner>/<repo>/edit/main/' },
		}),
	],
});
