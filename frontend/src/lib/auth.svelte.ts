type User = { id: string; email: string };
type AuthStatus = 'checking' | 'authenticated' | 'anonymous' | 'error';

let user = $state<User | null>(null);
let status = $state<AuthStatus>('checking');
let pendingCheck: Promise<void> | null = null;

async function responseError(response: Response, fallback: string): Promise<Error> {
	const body = await response.json().catch(() => null);
	return new Error(body?.message ?? fallback);
}

// 保護された API はこの関数を通し、期限切れを画面全体へ反映する。
export async function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
	if (!path.startsWith('/api/')) throw new Error('API の URL が不正です。');
	const response = await fetch(path, { ...init, credentials: 'same-origin', cache: 'no-store' });
	if (response.status === 401) {
		user = null;
		status = 'anonymous';
	}
	return response;
}

async function post(path: string, body?: unknown): Promise<Response> {
	// ログイン前後でトークンの対象ユーザーが変わるため、更新操作の直前に取得する。
	const csrf = await apiFetch('/api/auth/csrf');
	if (!csrf.ok) throw new Error('認証の準備に失敗しました。再試行してください。');
	const { token } = await csrf.json().catch(() => ({ token: undefined }));
	if (!token) throw new Error('認証の準備に失敗しました。再試行してください。');
	return apiFetch(path, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
		body: body === undefined ? undefined : JSON.stringify(body)
	});
}

export const auth = {
	get user() { return user; },
	get status() { return status; },
	get isLoggedIn() { return status === 'authenticated'; },
	check(): Promise<void> {
		if (pendingCheck) return pendingCheck;
		status = 'checking';
		pendingCheck = (async () => {
			try {
				const response = await apiFetch('/api/auth/me');
				if (response.status === 401) return;
				if (!response.ok) throw new Error('認証状態を確認できませんでした。');
				user = await response.json();
				status = 'authenticated';
			} catch {
				status = 'error';
			} finally {
				pendingCheck = null;
			}
		})();
		return pendingCheck;
	},
	async login(email: string, password: string) {
		const response = await post('/api/auth/login', { email, password });
		if (!response.ok) throw await responseError(response, 'ログインできませんでした。');
		user = await response.json();
		status = 'authenticated';
	},
	async signup(email: string, password: string) {
		const response = await post('/api/auth/register', { email, password });
		if (!response.ok) throw await responseError(response, 'アカウントを登録できませんでした。');
	},
	async logout() {
		const response = await post('/api/auth/logout');
		// サーバー側で Cookie は既に削除されているため、応答の成否に関わらずローカル状態は解除する。
		user = null;
		status = 'anonymous';
		if (!response.ok) throw await responseError(response, 'ログアウトできませんでした。');
	}
};
