import { visit } from 'unist-util-visit';

// GitHub のアラート記法（> [!NOTE] など）を Starlight の aside に対応付ける。
// Starlight が持つ variant は note / tip / caution / danger の 4 種類のみ。
const VARIANT_BY_ALERT = {
	NOTE: 'note',
	TIP: 'tip',
	IMPORTANT: 'note',
	WARNING: 'caution',
	CAUTION: 'danger',
};

const ALERT_PATTERN = /^\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\][ \t]*(\r?\n)?/;

/**
 * GitHub 形式のアラート（`> [!TIP]` 等）を Starlight の aside へ変換する remark プラグイン。
 *
 * Starlight は `:::tip` 形式の container directive のみを aside として扱うため、
 * 素の状態では `[!TIP]` という文字列がそのまま本文に出力されてしまう。
 * このプラグインは blockquote を containerDirective へ置き換えることで、
 * Starlight 側の remark-asides に処理を引き継がせる。
 *
 * Starlight のプラグインは配列の後方に push されるため、
 * astro.config.mjs の remarkPlugins に登録すれば必ず本プラグインが先に走る。
 */
export function remarkGithubAlerts() {
	return (tree) => {
		visit(tree, 'blockquote', (node, index, parent) => {
			if (!parent || index === undefined) return;

			const firstChild = node.children[0];
			if (firstChild?.type !== 'paragraph') return;

			const firstText = firstChild.children[0];
			if (firstText?.type !== 'text') return;

			const match = firstText.value.match(ALERT_PATTERN);
			if (!match) return;

			const variant = VARIANT_BY_ALERT[match[1]];

			// マーカー部分だけを取り除く。同じ段落に本文が続く場合はそれを残す。
			firstText.value = firstText.value.slice(match[0].length);
			if (firstText.value === '') firstChild.children.shift();
			if (firstChild.children.length === 0) node.children.shift();

			parent.children[index] = {
				type: 'containerDirective',
				name: variant,
				attributes: {},
				children: node.children,
			};
		});
	};
}
