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

function userListFixture() {
	return {
		items: [
			{ id: 'user-1', email: 'alice@example.com', createdAt: '2026-09-01T00:00:00Z' },
			{ id: 'user-2', email: 'bob@example.com', createdAt: null }
		],
		totalCount: 2,
		page: 1,
		pageSize: 20
	};
}

describe('admin users page', () => {
	let fetchMock: ReturnType<typeof vi.fn<FetchHandler>>;
	let usersStatus: number;
	let lastUsersUrl: string | undefined;

	beforeEach(async () => {
		usersStatus = 200;
		lastUsersUrl = undefined;
		gotoMock.mockClear();
		fetchMock = vi.fn<FetchHandler>(async (path) => {
			if (path === '/api/auth/csrf') return jsonResponse(200, { token: 'csrf-token' });
			if (path === '/api/auth/login') {
				return jsonResponse(200, { id: 'admin-1', email: 'admin@example.com', permissions: { 'Admin.Users': 1 } });
			}
			if (path.startsWith('/api/admin/users')) {
				lastUsersUrl = path;
				return usersStatus === 200 ? jsonResponse(200, userListFixture()) : jsonResponse(usersStatus, {});
			}
			throw new Error(`未対応のリクエスト: ${path}`);
		});
		vi.stubGlobal('fetch', fetchMock);
		await auth.login('admin@example.com', 'Password-123!ABC');
	});

	afterEach(() => {
		vi.restoreAllMocks();
		vi.unstubAllGlobals();
	});

	it('ユーザー一覧を取得して表示する', async () => {
		render(Page);
		expect(await screen.findByText('alice@example.com')).toBeTruthy();
		expect(screen.getByText('bob@example.com')).toBeTruthy();
	});

	it('登録日時が不明なユーザーは-を表示する', async () => {
		render(Page);
		await screen.findByText('bob@example.com');
		const row = screen.getByText('bob@example.com').closest('tr');
		expect(row?.textContent).toContain('-');
	});

	it('検索フォームからメールアドレスで絞り込む', async () => {
		render(Page);
		await screen.findByText('alice@example.com');

		await fireEvent.input(screen.getByLabelText('メールアドレスで検索'), { target: { value: 'alice' } });
		await fireEvent.click(screen.getByRole('button', { name: '検索' }));

		await waitFor(() => expect(lastUsersUrl).toContain('email=alice'));
	});

	it('前方一致・大文字小文字を区別のオプションを付けて検索する', async () => {
		render(Page);
		await screen.findByText('alice@example.com');

		await fireEvent.input(screen.getByLabelText('メールアドレスで検索'), { target: { value: 'alice' } });
		await fireEvent.click(screen.getByLabelText('前方一致検索'));
		await fireEvent.click(screen.getByLabelText('大文字小文字を区別'));
		await fireEvent.click(screen.getByRole('button', { name: '検索' }));

		await waitFor(() => expect(lastUsersUrl).toContain('prefixMatch=true'));
		expect(lastUsersUrl).toContain('caseSensitive=true');
	});

	it('列見出しをクリックするとソート条件付きで再取得する', async () => {
		render(Page);
		await screen.findByText('alice@example.com');

		await fireEvent.click(screen.getByRole('button', { name: /メールアドレス/ }));

		await waitFor(() => expect(lastUsersUrl).toContain('sort=email'));
		expect(lastUsersUrl).toContain('direction=ascending');
	});

	it('権限不足の場合は操作UIを出さず案内を表示する', async () => {
		usersStatus = 403;
		render(Page);

		expect((await screen.findByRole('alert')).textContent).toContain('権限がありません');
		expect(screen.queryByLabelText('メールアドレスで検索')).toBeNull();
	});
});
