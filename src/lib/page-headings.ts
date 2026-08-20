import { getCollection, render } from 'astro:content';
import type { MarkdownHeading } from 'astro';

/** サイドバーに出さない見出し。各ページ冒頭の目次は右 TOC と重複するため除外する。 */
const EXCLUDED_HEADINGS = ['目次'];

/** サイドバーに展開する見出しの深さ。2 = Markdown の `##`。 */
const HEADING_DEPTH = 2;

let cache: Map<string, MarkdownHeading[]> | null = null;

/** 先頭・末尾のスラッシュと末尾の `index` を落として比較用のキーにする。 */
export function normalizeSlug(value: string): string {
	return value.replace(/^\/+|\/+$/g, '').replace(/(^|\/)index$/, '');
}

/** サイドバーのリンク href を、コンテンツコレクションの id と同じ形に揃える。 */
export function hrefToSlug(href: string): string {
	const base = normalizeSlug(import.meta.env.BASE_URL);
	let path = normalizeSlug(href);
	if (base && (path === base || path.startsWith(`${base}/`))) {
		path = path.slice(base.length);
	}
	return normalizeSlug(path);
}

/**
 * 全ページの `##` 見出しを slug 単位で集める。
 * ビルド中に何度も呼ばれるため、モジュールスコープで結果を保持する。
 */
export async function getPageHeadings(): Promise<Map<string, MarkdownHeading[]>> {
	if (cache) return cache;

	const map = new Map<string, MarkdownHeading[]>();
	for (const entry of await getCollection('docs')) {
		const { headings } = await render(entry);
		map.set(
			normalizeSlug(entry.id),
			headings.filter(
				(heading) =>
					heading.depth === HEADING_DEPTH && !EXCLUDED_HEADINGS.includes(heading.text.trim())
			)
		);
	}

	cache = map;
	return map;
}
