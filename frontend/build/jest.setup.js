require('@testing-library/jest-dom');

if (!window.Bibliophilarr) {
	window.Bibliophilarr = {
		apiRoot: '/api',
		version: 'test',
		branch: 'test'
	};
}
