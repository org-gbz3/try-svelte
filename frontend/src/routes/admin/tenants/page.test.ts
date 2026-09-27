import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const { gotoMock } = vi.hoisted(() => ({ gotoMock: vi.fn() }));
vi.mock('$app/navigation', () => ({ goto: gotoMock }));

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

describe('admin tenants page', () => {
	let fetchMock: ReturnType<typeof vi.fn<FetchHandler>>;
	let listStatus: number;
	let tenantPermission: number;
	let tenants: { id: string; name: string; createdAt: string; memberCount: number }[];

	async function loginAs(permission: number) {
		tenantPermission = permission;
		await auth.login('admin@example.com', 'Password-123!ABC');
	}

	beforeEach(() => {
		listStatus = 200;
		tenants = [{ id: 'tenant-1', name: 'テナントA', createdAt: '2026-09-01T00:00:00Z', memberCount: 3 }];
		gotoMock.mockClear();
		fetchMock = vi.fn<FetchHandler>(async (path, init) => {
			const method = init?.method ?? 'GET';
			if (path === '/api/auth/csrf') return jsonResponse(200, { token: 'csrf-token' });
			if (path === '/api/auth/login') {
				return jsonResponse(200, { id: 'admin-1', email: 'admin@example.com', permissions: { 'Admin.Tenants': tenantPermission } });
			}
			if (path === '/api/admin/tenants' && method === 'GET') {
				return listStatus === 200 ? jsonResponse(200, tenants) : jsonResponse(listStatus, {});
			}
			if (path === '/api/admin/tenants' && method === 'POST') {
				const { name } = JSON.parse(String(init?.body)) as { name: string };
				tenants = [...tenants, { id: 'tenant-2', name, createdAt: '2026-09-02T00:00:00Z', memberCount: 0 }];
				return jsonResponse(201, { id: 'tenant-2' });
			}
			throw new Error(`未対応のリクエスト: ${method} ${path}`);
		});
		vi.stubGlobal('fetch', fetchMock);
	});

	afterEach(() => {
		vi.restoreAllMocks();
		vi.unstubAllGlobals();
	});

	it('テナント一覧を詳細へのリンクと所属人数付きで表示する', async () => {
		await loginAs(1);
		render(Page);
		const link = await screen.findByRole('link', { name: 'テナントA' });
		expect(link.getAttribute('href')).toBe('/admin/tenants/tenant-1');
		expect(screen.getByRole('cell', { name: '3' })).toBeTruthy();
	});

	it('Read権限のみの場合は作成フォームを表示しない', async () => {
		await loginAs(1);
		render(Page);
		await screen.findByRole('link', { name: 'テナントA' });
		expect(screen.queryByRole('button', { name: '作成' })).toBeNull();
	});

	it('テナントを作成すると一覧に追加する', async () => {
		await loginAs(2);
		render(Page);
		await fireEvent.input(await screen.findByLabelText('新規テナント名'), { target: { value: 'テナントB' } });
		await fireEvent.click(screen.getByRole('button', { name: '作成' }));

		expect(await screen.findByRole('link', { name: 'テナントB' })).toBeTruthy();
		await waitFor(() =>
			expect(fetchMock).toHaveBeenCalledWith(
				'/api/admin/tenants',
				expect.objectContaining({ method: 'POST', body: JSON.stringify({ name: 'テナントB' }) })
			)
		);
	});

	it('権限がない場合は権限不足を表示する', async () => {
		listStatus = 403;
		await loginAs(1);
		render(Page);
		expect((await screen.findByRole('alert')).textContent).toContain('権限がありません');
	});
});
