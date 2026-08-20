import path from 'node:path';

/**
 * Markdown 内の相対 .md リンクを、公開サイトの URL に書き換える rehype プラグイン。
 *
 * 例) src/content/docs/index.md の `./guides/installation.md`
 *     → `/<base>/guides/installation/`
 *
 * これにより「GitHub 上で md を見たときもリンクが辿れる」状態を保ったまま、
 * 公開サイトでも正しい URL になる。
 */
export function rehypeMarkdownLinks({ docsDir, base = '/' } = {}) {
	const docsRoot = path.resolve(docsDir ?? 'src/content/docs');
	const basePrefix = base === '/' ? '' : base.replace(/\/$/, '');

	return (tree, file) => {
		const filePath = file?.path ?? file?.history?.[0];
		if (!filePath) return;
		const fileDir = path.dirname(path.resolve(filePath));

		walk(tree, (node) => {
			if (node.type !== 'element' || node.tagName !== 'a') return;
			const href = node.properties?.href;
			if (typeof href !== 'string') return;

			// 外部リンク・ページ内アンカー・絶対パスは対象外
			if (/^([a-z][a-z0-9+.-]*:|\/\/|#|\/)/i.test(href)) return;

			const [pathname, tail] = splitSuffix(href);
			if (!/\.mdx?$/i.test(pathname)) return;

			const target = path.resolve(fileDir, pathname);
			let slug = path
				.relative(docsRoot, target)
				.replace(/\\/g, '/')
				.replace(/\.mdx?$/i, '');

			// docs 配下を外れる参照は書き換えない
			if (slug.startsWith('..')) return;

			slug = slug.replace(/(^|\/)index$/, '');
			const url = `${basePrefix}/${slug}${slug ? '/' : ''}`.replace(/\/{2,}/g, '/');
			node.properties.href = url + tail;
		});
	};
}

function splitSuffix(href) {
	const match = href.match(/^([^#?]*)([#?].*)?$/);
	return [match?.[1] ?? href, match?.[2] ?? ''];
}

function walk(node, fn) {
	fn(node);
	if (Array.isArray(node.children)) {
		for (const child of node.children) walk(child, fn);
	}
}
