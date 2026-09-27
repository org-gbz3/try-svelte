import { render, screen, fireEvent } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const { gotoMock } = vi.hoisted(() => ({ gotoMock: vi.fn() }));
vi.mock('$app/navigation', () => ({ goto: gotoMock }));
vi.mock('$app/state', () => ({
	page: { params: { tenantId: 'tenant-1' }, url: new URL('http://localhost/t/tenant-1/settings/roles') }
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

describe('tenant roles page', () => {
	let fetchMock: ReturnType<typeof vi.fn<FetchHandler>>;

	beforeEach(async () => {
		gotoMock.mockClear();
		fetchMock = vi.fn<FetchHandler>(async (path, init) => {
			const method = init?.method ?? 'GET';
			if (path === '/api/auth/csrf') return jsonResponse(200, { token: 'csrf-token' });
			if (path === '/api/auth/login') {
				return jsonResponse(200, { id: 'user-1', email: 'admin@example.com', permissions: {}, tenants: [] });
			}
			if (path === '/api/tenants/tenant-1/roles' && method === 'GET') {
				return jsonResponse(200, [
					{ id: 'role-1', name: 'テナント管理者', isDefaultAdmin: true, permissions: [{ actionKey: 'Tenant.Roles', displayName: 'テナントロール管理', level: 2 }] },
					{ id: 'role-2', name: '閲覧者', isDefaultAdmin: false, permissions: [] }
				]);
			}
			if (path === '/api/tenants/tenant-1/roles/permission-actions') {
				return jsonResponse(200, [{ actionKey: 'Tenant.Roles', displayName: 'テナントロール管理' }]);
			}
			throw new Error(`未対応のリクエスト: ${method} ${path}`);
		});
		vi.stubGlobal('fetch', fetchMock);
		await auth.login('admin@example.com', 'Password-123!ABC');
	});

	afterEach(() => {
		vi.restoreAllMocks();
		vi.unstubAllGlobals();
	});

	it('テナントのロールAPIからロール一覧を表示する', async () => {
		render(Page);
		expect(await screen.findByRole('button', { name: '閲覧者' })).toBeTruthy();
		expect(fetchMock).toHaveBeenCalledWith('/api/tenants/tenant-1/roles', expect.anything());
	});

	it('既定のテナント管理者ロールは削除・権限保存の操作を表示しない', async () => {
		render(Page);
		await fireEvent.click(await screen.findByRole('button', { name: 'テナント管理者' }));
		expect(screen.getByText(/削除・権限の変更はできません/)).toBeTruthy();
		expect(screen.queryByRole('button', { name: 'ロールを削除' })).toBeNull();
		expect(screen.queryByRole('button', { name: '権限を保存' })).toBeNull();
	});

	it('既定以外のロールは削除・権限保存の操作を表示する', async () => {
		render(Page);
		await fireEvent.click(await screen.findByRole('button', { name: '閲覧者' }));
		expect(screen.getByRole('button', { name: 'ロールを削除' })).toBeTruthy();
		expect(screen.getByRole('button', { name: '権限を保存' })).toBeTruthy();
	});
});
