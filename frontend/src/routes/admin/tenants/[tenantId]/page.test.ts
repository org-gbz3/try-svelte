import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const { gotoMock } = vi.hoisted(() => ({ gotoMock: vi.fn() }));
vi.mock('$app/navigation', () => ({ goto: gotoMock }));
vi.mock('$app/state', () => ({
	page: { params: { tenantId: 'tenant-1' }, url: new URL('http://localhost/admin/tenants/tenant-1') }
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

describe('admin tenant detail page', () => {
	let fetchMock: ReturnType<typeof vi.fn<FetchHandler>>;
	let detailStatus: number;
	let members: { userId: string; email: string; roleIds: string[] }[];
	let addStatus: number;

	beforeEach(async () => {
		detailStatus = 200;
		addStatus = 201;
		members = [{ userId: 'user-1', email: 'member@example.com', roleIds: [] }];
		gotoMock.mockClear();
		vi.stubGlobal('confirm', vi.fn(() => true));
		fetchMock = vi.fn<FetchHandler>(async (path, init) => {
			const method = init?.method ?? 'GET';
			if (path === '/api/auth/csrf') return jsonResponse(200, { token: 'csrf-token' });
			if (path === '/api/auth/login') {
				return jsonResponse(200, { id: 'admin-1', email: 'admin@example.com', permissions: { 'Admin.Tenants': 2 } });
			}
			if (path === '/api/admin/tenants/tenant-1' && method === 'GET') {
				return detailStatus === 200
					? jsonResponse(200, {
						id: 'tenant-1',
						name: 'テナントA',
						createdAt: '2026-09-01T00:00:00Z',
						roles: [{ id: 'role-1', name: 'テナント管理者' }],
						members
					})
					: jsonResponse(detailStatus, {});
			}
			if (path === '/api/admin/tenants/tenant-1' && method === 'DELETE') return jsonResponse(204, {});
			if (path === '/api/admin/tenants/tenant-1/members' && method === 'POST') {
				if (addStatus !== 201) return jsonResponse(addStatus, { message: 'このメールアドレスのユーザーは見つかりません。' });
				members = [...members, { userId: 'user-2', email: 'new@example.com', roleIds: [] }];
				return jsonResponse(201, { userId: 'user-2' });
			}
			if (path === '/api/admin/tenants/tenant-1/members/user-1/roles' && method === 'PUT') {
				members = members.map((member) => member.userId === 'user-1'
					? { ...member, roleIds: (JSON.parse(String(init?.body)) as { roleIds: string[] }).roleIds }
					: member);
				return jsonResponse(204, {});
			}
			if (path === '/api/admin/tenants/tenant-1/members/user-1' && method === 'DELETE') {
				members = members.filter((member) => member.userId !== 'user-1');
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

	it('メンバーにテナントロールを割り当てて保存するとPUTで送信する', async () => {
		render(Page);
		await fireEvent.click(await screen.findByRole('checkbox', { name: 'member@example.com: テナント管理者' }));
		await fireEvent.click(screen.getByRole('button', { name: 'member@example.com のロールを保存' }));

		await waitFor(() =>
			expect(fetchMock).toHaveBeenCalledWith(
				'/api/admin/tenants/tenant-1/members/user-1/roles',
				expect.objectContaining({ method: 'PUT', body: JSON.stringify({ roleIds: ['role-1'] }) })
			)
		);
		expect(await screen.findByRole('status')).toBeTruthy();
	});

	it('メールアドレスでメンバーを追加すると一覧に表示する', async () => {
		render(Page);
		await fireEvent.input(await screen.findByLabelText('追加するユーザーのメールアドレス'), { target: { value: 'new@example.com' } });
		await fireEvent.click(screen.getByRole('button', { name: '追加' }));
		expect(await screen.findByText('new@example.com')).toBeTruthy();
	});

	it('メンバーを追加できない場合はサーバーの理由を表示する', async () => {
		addStatus = 400;
		render(Page);
		await fireEvent.input(await screen.findByLabelText('追加するユーザーのメールアドレス'), { target: { value: 'nobody@example.com' } });
		await fireEvent.click(screen.getByRole('button', { name: '追加' }));
		expect((await screen.findByRole('alert')).textContent).toContain('見つかりません');
	});

	it('所属を解除するとメンバー一覧から外す', async () => {
		render(Page);
		await fireEvent.click(await screen.findByRole('button', { name: 'member@example.com の所属を解除' }));
		expect(await screen.findByText('メンバーがまだいません。')).toBeTruthy();
	});

	it('テナントを削除すると一覧画面へ移動する', async () => {
		render(Page);
		await fireEvent.click(await screen.findByRole('button', { name: 'テナントを削除' }));
		await waitFor(() => expect(gotoMock).toHaveBeenCalledWith('/admin/tenants'));
	});

	it('存在しないテナントの場合は専用のメッセージを表示する', async () => {
		detailStatus = 404;
		render(Page);
		expect((await screen.findByRole('alert')).textContent).toContain('見つかりません');
	});
});
