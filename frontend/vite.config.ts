import adapter from '@sveltejs/adapter-static';
import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vite';

export default defineConfig({
	plugins: [
		sveltekit({
			compilerOptions: {
				// Force runes mode for the project, except for libraries. Can be removed in svelte 6.
				runes: ({ filename }) => filename.split(/[/\\]/).includes('node_modules') ? undefined : true
			},
			adapter: adapter({
				pages: '../backend/wwwroot',
				assets: '../backend/wwwroot',
				fallback: 'index.html'
			}),
			csp: {
				mode: 'auto',
				directives: {
					'default-src': ['self'],
					'style-src': ['self', 'https://fonts.googleapis.com'],
					// SvelteKit の読み上げ通知要素の固定スタイルだけを許可する。
					// Kit 更新時にスタイルが変わった場合は、このハッシュも再確認する。
					'style-src-attr': [
						'unsafe-hashes',
						'sha256-S8qMpvofolR8Mpjy4kQvEm7m1q8clzU4dfDH0AmvZjo='
					],
					'font-src': ['self', 'https://fonts.gstatic.com'],
					'img-src': ['self', 'data:']
				}
			},
		})
	],
	server: {
		host: true,
		proxy: {
			'/api': 'http://localhost:5000'
		}
	}
});
