import { render, screen } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const { gotoMock } = vi.hoisted(() => ({ gotoMock: vi.fn() }));
vi.mock('$app/navigation', () => ({ goto: gotoMock }));
vi.mock('$app/state', () => ({
	page: { params: { tenantId: 'tenant-1' }, url: new URL('http://localhost/t/tenant-1') }
}));

import { auth } from '$lib/auth.svelte';
import Page from './+page.svelte';

type FetchHandler = (path: string, init?: RequestInit) => Response | Promise<Response>;

function jsonResponse(status: number, body: unknown): Response {
	return {
		ok: status >= 200 && status < 300,
		status,
		json: async () => body
	} as unknown as Response;
}

describe('tenant home page', () => {
	let meStatus: number;
	let tenantPermissions: Record<string, number>;

	beforeEach(async () => {
		meStatus = 200;
		tenantPermissions = {};
		gotoMock.mockClear();
		vi.stubGlobal('fetch', vi.fn<FetchHandler>(async (path) => {
			if (path === '/api/auth/csrf') return jsonResponse(200, { token: 'csrf-token' });
			if (path === '/api/auth/login') {
				return jsonResponse(200, {
					id: 'user-1', email: 'user@example.com', permissions: {},
					tenants: [{ id: 'tenant-1', name: 'テナントA' }]
				});
			}
			if (path === '/api/tenants/tenant-1/me') {
				return meStatus === 200
					? jsonResponse(200, { id: 'tenant-1', name: 'テナントA', permissions: tenantPermissions })
					: jsonResponse(meStatus, {});
			}
			throw new Error(`未対応のリクエスト: ${path}`);
		}));
		await auth.login('user@example.com', 'Password-123!ABC');
	});

	afterEach(() => {
		vi.restoreAllMocks();
		vi.unstubAllGlobals();
	});

	it('テナントの権限に応じて管理画面へのリンクを表示する', async () => {
		tenantPermissions = { 'Tenant.Roles': 2 };
		render(Page);
		expect(await screen.findByRole('heading', { name: 'テナントA' })).toBeTruthy();
		expect(screen.getByRole('link', { name: 'テナントロール管理' }).getAttribute('href')).toBe('/t/tenant-1/settings/roles');
		expect(screen.queryByRole('link', { name: 'メンバーのロール割り当て' })).toBeNull();
	});

	it('テナントの権限がない場合は利用できる機能がないことを表示する', async () => {
		render(Page);
		expect(await screen.findByText('利用できる機能はまだありません。')).toBeTruthy();
	});

	it('所属していないテナントの場合は表示できないことを案内する', async () => {
		meStatus = 403;
		render(Page);
		expect((await screen.findByRole('alert')).textContent).toContain('所属していない');
	});
});
