import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const { gotoMock } = vi.hoisted(() => ({ gotoMock: vi.fn() }));
vi.mock('$app/navigation', () => ({ goto: gotoMock }));
vi.mock('$app/state', () => ({
	page: { params: { tenantId: 'tenant-1' }, url: new URL('http://localhost/t/tenant-1/settings/members') }
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

describe('tenant members page', () => {
	let fetchMock: ReturnType<typeof vi.fn<FetchHandler>>;
	let listStatus: number;
	let saveStatus: number;
	let members: { userId: string; email: string; roleIds: string[] }[];

	beforeEach(async () => {
		listStatus = 200;
		saveStatus = 204;
		members = [{ userId: 'user-2', email: 'member@example.com', roleIds: ['role-1'] }];
		gotoMock.mockClear();
		fetchMock = vi.fn<FetchHandler>(async (path, init) => {
			const method = init?.method ?? 'GET';
			if (path === '/api/auth/csrf') return jsonResponse(200, { token: 'csrf-token' });
			if (path === '/api/auth/login') {
				return jsonResponse(200, { id: 'user-1', email: 'admin@example.com', permissions: {}, tenants: [] });
			}
			if (path === '/api/tenants/tenant-1/members' && method === 'GET') {
				return listStatus === 200
					? jsonResponse(200, {
						roles: [{ id: 'role-1', name: 'テナント管理者' }, { id: 'role-2', name: '閲覧者' }],
						members
					})
					: jsonResponse(listStatus, {});
			}
			if (path === '/api/tenants/tenant-1/members/user-2/roles' && method === 'PUT') {
				if (saveStatus !== 204) return jsonResponse(saveStatus, {});
				members = [{ ...members[0], roleIds: (JSON.parse(String(init?.body)) as { roleIds: string[] }).roleIds }];
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

	it('割り当て済みのテナントロールをチェック状態で表示する', async () => {
		render(Page);
		expect(await screen.findByRole('checkbox', { name: 'member@example.com: テナント管理者' })).toHaveProperty('checked', true);
		expect(screen.getByRole('checkbox', { name: 'member@example.com: 閲覧者' })).toHaveProperty('checked', false);
	});

	it('チェックを変更して保存するとPUTで送信する', async () => {
		render(Page);
		await fireEvent.click(await screen.findByRole('checkbox', { name: 'member@example.com: 閲覧者' }));
		await fireEvent.click(screen.getByRole('button', { name: 'member@example.com のロールを保存' }));

		await waitFor(() =>
			expect(fetchMock).toHaveBeenCalledWith(
				'/api/tenants/tenant-1/members/user-2/roles',
				expect.objectContaining({ method: 'PUT', body: JSON.stringify({ roleIds: ['role-1', 'role-2'] }) })
			)
		);
		expect(await screen.findByRole('status')).toBeTruthy();
	});

	it('参照権限のみで保存した場合は権限がないことを表示する', async () => {
		saveStatus = 403;
		render(Page);
		await fireEvent.click(await screen.findByRole('button', { name: 'member@example.com のロールを保存' }));
		expect((await screen.findByRole('alert')).textContent).toContain('権限がありません');
	});

	it('一覧を取得する権限がない場合は権限不足を表示する', async () => {
		listStatus = 403;
		render(Page);
		expect((await screen.findByRole('alert')).textContent).toContain('権限がありません');
	});
});
