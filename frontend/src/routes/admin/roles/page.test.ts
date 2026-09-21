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

const permissionActions = [
	{ actionKey: 'Admin.Roles', displayName: 'ロール管理' },
	{ actionKey: 'WeatherForecast.Get', displayName: '天気予報の取得' }
];

function roleFixture() {
	return [
		{
			id: 'role-1',
			name: 'viewer',
			permissions: [{ actionKey: 'WeatherForecast.Get', displayName: '天気予報の取得', level: 1 }]
		}
	];
}

describe('admin roles page', () => {
	let fetchMock: ReturnType<typeof vi.fn<FetchHandler>>;
	let roles: ReturnType<typeof roleFixture>;
	let rolesStatus: number;

	beforeEach(async () => {
		roles = roleFixture();
		rolesStatus = 200;
		gotoMock.mockClear();
		fetchMock = vi.fn<FetchHandler>(async (path, init) => {
			const method = init?.method ?? 'GET';
			if (path === '/api/auth/csrf') return jsonResponse(200, { token: 'csrf-token' });
			if (path === '/api/auth/login') {
				return jsonResponse(200, { id: 'admin-1', email: 'admin@example.com', permissions: { 'Admin.Roles': 2 } });
			}
			if (path === '/api/admin/roles' && method === 'GET') {
				return rolesStatus === 200 ? jsonResponse(200, roles) : jsonResponse(rolesStatus, {});
			}
			if (path === '/api/admin/roles/permission-actions' && method === 'GET') {
				return rolesStatus === 200 ? jsonResponse(200, permissionActions) : jsonResponse(rolesStatus, {});
			}
			if (path === '/api/admin/roles' && method === 'POST') {
				roles = [...roles, { id: 'role-2', name: 'new-role', permissions: [] }];
				return jsonResponse(201, {});
			}
			if (path === '/api/admin/roles/role-1/permissions' && method === 'PUT') {
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

	it('ロール一覧を取得して表示し、選択すると権限マトリクスに全アクションが表示される', async () => {
		render(Page);
		await fireEvent.click(await screen.findByRole('button', { name: 'viewer' }));

		expect(await screen.findByRole('rowheader', { name: 'ロール管理' })).toBeTruthy();
		expect(screen.getByRole('rowheader', { name: '天気予報の取得' })).toBeTruthy();
	});

	it('新規ロール作成フォームからPOSTを呼び、一覧を再取得する', async () => {
		render(Page);
		await screen.findByRole('button', { name: 'viewer' });

		await fireEvent.input(screen.getByLabelText('新規ロール名'), { target: { value: 'new-role' } });
		await fireEvent.click(screen.getByRole('button', { name: '作成' }));

		await waitFor(() =>
			expect(fetchMock).toHaveBeenCalledWith(
				'/api/admin/roles',
				expect.objectContaining({ method: 'POST', body: JSON.stringify({ name: 'new-role' }) })
			)
		);
		expect(await screen.findByRole('button', { name: 'new-role' })).toBeTruthy();
	});

	it('権限マトリクスの変更をPUTで保存する', async () => {
		render(Page);
		await fireEvent.click(await screen.findByRole('button', { name: 'viewer' }));

		await fireEvent.click(await screen.findByRole('radio', { name: '天気予報の取得: 編集' }));
		await fireEvent.click(screen.getByRole('button', { name: '権限を保存' }));

		await waitFor(() =>
			expect(fetchMock).toHaveBeenCalledWith(
				'/api/admin/roles/role-1/permissions',
				expect.objectContaining({
					method: 'PUT',
					body: JSON.stringify({
						permissions: [
							{ actionKey: 'Admin.Roles', level: 0 },
							{ actionKey: 'WeatherForecast.Get', level: 2 }
						]
					})
				})
			)
		);
	});

	it('権限不足の場合は操作UIを出さず案内を表示する', async () => {
		rolesStatus = 403;
		render(Page);

		expect((await screen.findByRole('alert')).textContent).toContain('権限がありません');
		expect(screen.queryByLabelText('新規ロール名')).toBeNull();
	});
});
