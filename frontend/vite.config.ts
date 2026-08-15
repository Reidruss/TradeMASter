import adapter from '@sveltejs/adapter-auto';
import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vite';

const backendUrl = process.env.BACKEND_URL || 'http://localhost:5126';

export default defineConfig({
	plugins: [
		sveltekit({
			compilerOptions: {
				runes: ({ filename }) =>
					filename.split(/[/\\]/).includes('node_modules') ? undefined : true
			},
			adapter: adapter()
		})
	],
	server: {
		port: 5173,
		strictPort: false,
		proxy: {
			// Proxy API requests directly to ASP.NET Core backend in dev
			'/api': {
				target: backendUrl,
				changeOrigin: true,
				secure: false
			},
			// Proxy OpenAPI and Scalar documentation UI
			'/openapi': {
				target: backendUrl,
				changeOrigin: true,
				secure: false
			},
			'/scalar': {
				target: backendUrl,
				changeOrigin: true,
				secure: false
			}
		}
	}
});
