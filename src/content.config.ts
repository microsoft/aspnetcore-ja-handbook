import { defineCollection } from 'astro:content';
import { docsLoader } from '@astrojs/starlight/loaders';
import { docsSchema } from '@astrojs/starlight/schema';

export const collections = {
	// Starlight 公式の既定パス src/content/docs/ を読む
	docs: defineCollection({
		loader: docsLoader(),
		schema: docsSchema(),
	}),
};
