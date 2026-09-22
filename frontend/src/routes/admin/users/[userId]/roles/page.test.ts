import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const { gotoMock } = vi.hoisted(() => ({ gotoMock: vi.fn() }));
vi.mock('$app/navigation', () => ({ goto: gotoMock }));
vi.mock('$app/state', () => ({
	page: {
		params: { userId: 'target-user' },
		url: new URL('http://localhost/admin/users/target-user/roles?email=target%40example.com')
	}
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

function rolesFixture() {
	return [
		{ id: 'role-1', name: 'viewer' },
		{ id: 'role-2', name: 'editor' }
	];
}

describe('admin user roles page', () => {
	let fetchMock: ReturnType<typeof vi.fn<FetchHandler>>;
	let userRolesStatus: number;
	let assignedRoles: string[];

	beforeEach(async () => {
		userRolesStatus = 200;
		assignedRoles = ['viewer'];
		gotoMock.mockClear();
		fetchMock = vi.fn<FetchHandler>(async (path, init) => {
			const method = init?.method ?? 'GET';
			if (path === '/api/auth/csrf') return jsonResponse(200, { token: 'csrf-token' });
			if (path === '/api/auth/login') {
				return jsonResponse(200, { id: 'admin-1', email: 'admin@example.com', permissions: { 'Admin.UserRoles': 2, 'Admin.Roles': 1 } });
			}
			if (path === '/api/admin/roles' && method === 'GET') {
				return jsonResponse(200, rolesFixture());
			}
			if (path === '/api/admin/users/target-user/roles' && method === 'GET') {
				return userRolesStatus === 200
					? jsonResponse(200, { userId: 'target-user', roles: assignedRoles })
					: jsonResponse(userRolesStatus, {});
			}
			if (path === '/api/admin/users/target-user/roles' && method === 'PUT') {
				assignedRoles = (JSON.parse(String(init?.body)) as { roles: string[] }).roles;
				return jsonResponse(204, {});
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

	it('割り当て済みのロールをチェック状態で表示する', async () => {
		render(Page);
		expect(await screen.findByRole('checkbox', { name: 'viewer' })).toHaveProperty('checked', true);
		expect(screen.getByRole('checkbox', { name: 'editor' })).toHaveProperty('checked', false);
	});

	it('チェックを変更して保存するとPUTで送信する', async () => {
		render(Page);
		await fireEvent.click(await screen.findByRole('checkbox', { name: 'editor' }));
		await fireEvent.click(screen.getByRole('button', { name: '保存' }));

		await waitFor(() =>
			expect(fetchMock).toHaveBeenCalledWith(
				'/api/admin/users/target-user/roles',
				expect.objectContaining({ method: 'PUT', body: JSON.stringify({ roles: ['viewer', 'editor'] }) })
			)
		);
	});

	it('存在しないユーザーの場合は専用のメッセージを表示する', async () => {
		userRolesStatus = 404;
		render(Page);
		expect((await screen.findByRole('alert')).textContent).toContain('見つかりません');
	});
});
