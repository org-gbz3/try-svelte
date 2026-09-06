import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { auth as Auth, apiFetch as ApiFetch } from './auth.svelte';

type FetchHandler = (path: string, init?: RequestInit) => Response | Promise<Response>;

function jsonResponse(status: number, body: unknown): Response {
	return {
		ok: status >= 200 && status < 300,
		status,
		json: async () => body
	} as unknown as Response;
}

function brokenJsonResponse(status: number): Response {
	return {
		ok: status >= 200 && status < 300,
		status,
		json: async () => {
			throw new SyntaxError('Unexpected token');
		}
	} as unknown as Response;
}

let auth: typeof Auth;
let apiFetch: typeof ApiFetch;
let fetchMock: ReturnType<typeof vi.fn<FetchHandler>>;

beforeEach(async () => {
	// $state はモジュールスコープの単一インスタンスなので、
	// テストごとにモジュールを再読み込みして状態をリセットする。
	vi.resetModules();
	const module = await import('./auth.svelte');
	auth = module.auth;
	apiFetch = module.apiFetch;
	fetchMock = vi.fn<FetchHandler>();
	vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
	vi.unstubAllGlobals();
});

const CSRF_OK = () => jsonResponse(200, { token: 'csrf-token' });

describe('apiFetch', () => {
	it('/api/ 以外のパスは拒否する', async () => {
		await expect(apiFetch('/other/path')).rejects.toThrow('API の URL が不正です。');
		expect(fetchMock).not.toHaveBeenCalled();
	});

	it('401 応答を受けるとログイン状態を解除する', async () => {
		fetchMock.mockResolvedValueOnce(CSRF_OK());
		fetchMock.mockResolvedValueOnce(jsonResponse(200, { id: '1', email: 'a@example.com' }));
		await auth.login('a@example.com', 'password');
		expect(auth.isLoggedIn).toBe(true);

		fetchMock.mockResolvedValueOnce(jsonResponse(401, {}));
		await apiFetch('/api/anything');

		expect(auth.user).toBeNull();
		expect(auth.status).toBe('anonymous');
	});
});

describe('post()の CSRF トークン検証', () => {
	it('CSRF 応答が JSON として解析できない場合は分かりやすいエラーを投げる', async () => {
		fetchMock.mockResolvedValueOnce(brokenJsonResponse(200));
		await expect(auth.login('a@example.com', 'password'))
			.rejects.toThrow('認証の準備に失敗しました。再試行してください。');
	});

	it('CSRF 応答にトークンが含まれない場合はエラーを投げる', async () => {
		fetchMock.mockResolvedValueOnce(jsonResponse(200, {}));
		await expect(auth.login('a@example.com', 'password'))
			.rejects.toThrow('認証の準備に失敗しました。再試行してください。');
	});
});

describe('auth.login', () => {
	it('成功するとユーザー情報を保持し authenticated になる', async () => {
		fetchMock.mockResolvedValueOnce(CSRF_OK());
		fetchMock.mockResolvedValueOnce(jsonResponse(200, { id: '1', email: 'a@example.com' }));

		await auth.login('a@example.com', 'password');

		expect(auth.user).toEqual({ id: '1', email: 'a@example.com' });
		expect(auth.status).toBe('authenticated');
	});

	it('失敗するとサーバーのメッセージで例外を投げる', async () => {
		fetchMock.mockResolvedValueOnce(CSRF_OK());
		fetchMock.mockResolvedValueOnce(jsonResponse(401, { message: 'ログインできません。' }));

		await expect(auth.login('a@example.com', 'wrong')).rejects.toThrow('ログインできません。');
		expect(auth.user).toBeNull();
	});
});

describe('auth.logout', () => {
	async function loginFirst() {
		fetchMock.mockResolvedValueOnce(CSRF_OK());
		fetchMock.mockResolvedValueOnce(jsonResponse(200, { id: '1', email: 'a@example.com' }));
		await auth.login('a@example.com', 'password');
	}

	it('成功すればユーザー情報を解除する', async () => {
		await loginFirst();
		fetchMock.mockResolvedValueOnce(CSRF_OK());
		fetchMock.mockResolvedValueOnce(jsonResponse(204, null));

		await auth.logout();

		expect(auth.user).toBeNull();
		expect(auth.status).toBe('anonymous');
	});

	it('サーバーが失敗応答を返しても、例外を投げつつローカル状態は解除する', async () => {
		await loginFirst();
		fetchMock.mockResolvedValueOnce(CSRF_OK());
		fetchMock.mockResolvedValueOnce(jsonResponse(500, { message: 'サーバーエラー' }));

		await expect(auth.logout()).rejects.toThrow('サーバーエラー');

		expect(auth.user).toBeNull();
		expect(auth.status).toBe('anonymous');
	});

	it('CSRF トークンの取得自体に失敗しても、ローカル状態は解除する', async () => {
		await loginFirst();
		fetchMock.mockRejectedValueOnce(new TypeError('network error'));

		await expect(auth.logout()).rejects.toThrow('network error');

		expect(auth.user).toBeNull();
		expect(auth.status).toBe('anonymous');
	});
});

describe('auth.check', () => {
	it('認証済みであればユーザー情報を取得する', async () => {
		fetchMock.mockResolvedValueOnce(jsonResponse(200, { id: '1', email: 'a@example.com' }));

		await auth.check();

		expect(auth.user).toEqual({ id: '1', email: 'a@example.com' });
		expect(auth.status).toBe('authenticated');
	});

	it('未ログインなら anonymous になる', async () => {
		fetchMock.mockResolvedValueOnce(jsonResponse(401, {}));

		await auth.check();

		expect(auth.status).toBe('anonymous');
	});

	it('通信エラー時は error になる', async () => {
		fetchMock.mockRejectedValueOnce(new TypeError('network error'));

		await auth.check();

		expect(auth.status).toBe('error');
	});

	it('同時に呼び出しても1回しか通信しない', async () => {
		fetchMock.mockResolvedValueOnce(jsonResponse(200, { id: '1', email: 'a@example.com' }));

		await Promise.all([auth.check(), auth.check()]);

		expect(fetchMock).toHaveBeenCalledTimes(1);
	});
});
