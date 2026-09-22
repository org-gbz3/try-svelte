import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type {
	auth as Auth,
	apiFetch as ApiFetch,
	csrfRequest as CsrfRequest,
	DEFAULT_API_TIMEOUT_MS as DefaultTimeout,
	DEFAULT_READ_RETRIES as DefaultReadRetries
} from './auth.svelte';

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
let csrfRequest: typeof CsrfRequest;
let DEFAULT_API_TIMEOUT_MS: typeof DefaultTimeout;
let DEFAULT_READ_RETRIES: typeof DefaultReadRetries;
let fetchMock: ReturnType<typeof vi.fn<FetchHandler>>;

beforeEach(async () => {
	// $state はモジュールスコープの単一インスタンスなので、
	// テストごとにモジュールを再読み込みして状態をリセットする。
	vi.resetModules();
	const module = await import('./auth.svelte');
	auth = module.auth;
	apiFetch = module.apiFetch;
	csrfRequest = module.csrfRequest;
	DEFAULT_API_TIMEOUT_MS = module.DEFAULT_API_TIMEOUT_MS;
	DEFAULT_READ_RETRIES = module.DEFAULT_READ_RETRIES;
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

describe('apiFetch のタイムアウト', () => {
	it('既定のタイムアウト値でAbortSignalを組み立てる', async () => {
		const timeoutSpy = vi.spyOn(AbortSignal, 'timeout');
		fetchMock.mockResolvedValueOnce(jsonResponse(200, {}));

		await apiFetch('/api/anything');

		expect(timeoutSpy).toHaveBeenCalledWith(DEFAULT_API_TIMEOUT_MS);
	});

	it('オプションで個別にタイムアウト値を上書きできる', async () => {
		const timeoutSpy = vi.spyOn(AbortSignal, 'timeout');
		fetchMock.mockResolvedValueOnce(jsonResponse(200, {}));

		await apiFetch('/api/anything', {}, { timeoutMs: 30_000 });

		expect(timeoutSpy).toHaveBeenCalledWith(30_000);
	});

	it('csrfRequest はCSRFトークン取得と本体の呼び出し両方に上書き値を伝える', async () => {
		const timeoutSpy = vi.spyOn(AbortSignal, 'timeout');
		fetchMock.mockResolvedValueOnce(CSRF_OK());
		fetchMock.mockResolvedValueOnce(jsonResponse(204, null));

		await csrfRequest('/api/anything', 'POST', undefined, { timeoutMs: 30_000 });

		expect(timeoutSpy).toHaveBeenCalledTimes(2);
		expect(timeoutSpy).toHaveBeenNthCalledWith(1, 30_000);
		expect(timeoutSpy).toHaveBeenNthCalledWith(2, 30_000);
	});

	it('タイムアウトで中断された場合は分かりやすいメッセージの例外を投げる', async () => {
		fetchMock.mockRejectedValue(new DOMException('The operation was aborted.', 'AbortError'));

		// リトライ挙動を混ぜず、メッセージ変換だけを確認する。
		await expect(apiFetch('/api/anything', {}, { retries: 0 }))
			.rejects.toThrow('通信がタイムアウトしました。時間をおいて再試行してください。');
	});

	it('タイムアウト以外の例外はそのまま投げる', async () => {
		fetchMock.mockRejectedValue(new TypeError('network error'));

		await expect(apiFetch('/api/anything', {}, { retries: 0 })).rejects.toThrow('network error');
	});
});

describe('apiFetch のリトライ', () => {
	it('参照系(GET)は初回とは別に既定で最大3回まで再試行し、途中で成功すればその応答を返す', async () => {
		fetchMock.mockRejectedValueOnce(new TypeError('network error'));
		fetchMock.mockRejectedValueOnce(new TypeError('network error'));
		fetchMock.mockResolvedValueOnce(jsonResponse(200, { ok: true }));

		const response = await apiFetch('/api/anything');

		expect(response.status).toBe(200);
		expect(fetchMock).toHaveBeenCalledTimes(3);
	});

	it('参照系(GET)は既定回数(初回+3回)を使い切ると最後のエラーを投げる', async () => {
		fetchMock.mockRejectedValue(new TypeError('network error'));

		await expect(apiFetch('/api/anything')).rejects.toThrow('network error');
		expect(fetchMock).toHaveBeenCalledTimes(DEFAULT_READ_RETRIES + 1);
	});

	it('更新系(POST等)は既定でリトライしない(初回のみ)', async () => {
		fetchMock.mockRejectedValue(new TypeError('network error'));

		await expect(apiFetch('/api/anything', { method: 'POST' })).rejects.toThrow('network error');
		expect(fetchMock).toHaveBeenCalledTimes(1);
	});

	it('retries オプションで既定値を個別に上書きできる', async () => {
		fetchMock.mockRejectedValue(new TypeError('network error'));

		await expect(apiFetch('/api/anything', { method: 'POST' }, { retries: 2 })).rejects.toThrow('network error');
		expect(fetchMock).toHaveBeenCalledTimes(3);
	});

	it('HTTP応答が返る失敗(4xx/5xx)は再試行の対象にしない', async () => {
		fetchMock.mockResolvedValueOnce(jsonResponse(500, { message: 'サーバーエラー' }));

		const response = await apiFetch('/api/anything');

		expect(response.status).toBe(500);
		expect(fetchMock).toHaveBeenCalledTimes(1);
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
		// CSRF取得(GET)は既定でリトライされるため、再試行時も一貫して失敗するようにする。
		fetchMock.mockRejectedValue(new TypeError('network error'));

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
		// /api/auth/me(GET)は既定でリトライされるため、再試行時も一貫して失敗するようにする。
		fetchMock.mockRejectedValue(new TypeError('network error'));

		await auth.check();

		expect(auth.status).toBe('error');
	});

	it('同時に呼び出しても1回しか通信しない', async () => {
		fetchMock.mockResolvedValueOnce(jsonResponse(200, { id: '1', email: 'a@example.com' }));

		await Promise.all([auth.check(), auth.check()]);

		expect(fetchMock).toHaveBeenCalledTimes(1);
	});
});
