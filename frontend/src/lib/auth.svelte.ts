// バックエンドの PermissionLevel(backend/Data/PermissionLevel.cs)と同じ数値。JSON上も数値でやり取りする。
export const PermissionLevel = { None: 0, Read: 1, Write: 2 } as const;
export type PermissionLevel = (typeof PermissionLevel)[keyof typeof PermissionLevel];

type User = { id: string; email: string; permissions: Record<string, PermissionLevel> };
type AuthStatus = 'checking' | 'authenticated' | 'anonymous' | 'error';

let user = $state<User | null>(null);
let status = $state<AuthStatus>('checking');
let pendingCheck: Promise<void> | null = null;

export async function responseError(response: Response, fallback: string): Promise<Error> {
	const body = await response.json().catch(() => null);
	return new Error(body?.message ?? fallback);
}

// 呼び出し元が個別に指定しない限り、全てのAPI呼び出しに適用する既定のタイムアウト値。
export const DEFAULT_API_TIMEOUT_MS = 10_000;

// 参照系(GET/HEAD)は再試行しても副作用が無いため、初回とは別に既定で複数回試す。
// 更新系は二重実行を避けたいため既定では再試行しない(0回)。
export const DEFAULT_READ_RETRIES = 3;
export const DEFAULT_WRITE_RETRIES = 0;

export type ApiFetchOptions = {
	timeoutMs?: number;
	/** 通信自体の失敗(タイムアウト・ネットワーク断)時に、初回アクセスとは別に再試行する回数。0でリトライしない。 */
	retries?: number;
};

// 保護された API はこの関数を通し、期限切れを画面全体へ反映する。
// timeoutMs・retries を省略すると既定値を使う。時間のかかるAPI・リトライ挙動を変えたいAPIだけ個別に上書きする。
export async function apiFetch(
	path: string,
	init: RequestInit = {},
	options: ApiFetchOptions = {}
): Promise<Response> {
	if (!path.startsWith('/api/')) throw new Error('API の URL が不正です。');
	const method = (init.method ?? 'GET').toUpperCase();
	const isReadMethod = method === 'GET' || method === 'HEAD';
	const timeoutMs = options.timeoutMs ?? DEFAULT_API_TIMEOUT_MS;
	const retries = options.retries ?? (isReadMethod ? DEFAULT_READ_RETRIES : DEFAULT_WRITE_RETRIES);
	const maxAttempts = retries + 1;

	for (let attempt = 1; ; attempt++) {
		try {
			const response = await fetch(path, {
				...init,
				credentials: 'same-origin',
				cache: 'no-store',
				signal: AbortSignal.timeout(timeoutMs)
			});
			if (response.status === 401) {
				user = null;
				status = 'anonymous';
			}
			return response;
		} catch (cause) {
			// HTTP応答が返った失敗(4xx/5xx)は再試行の対象にしない。ここに来るのは
			// タイムアウト・ネットワーク断など、通信そのものが成立しなかった場合のみ。
			if (attempt < maxAttempts) continue;
			// AbortSignal.timeout による中断は DOMException(AbortError)で、呼び出し元の
			// `cause instanceof Error` 判定に乗らないため、他のエラーと同様に扱えるよう変換する。
			if (cause instanceof DOMException && cause.name === 'AbortError') {
				throw new Error('通信がタイムアウトしました。時間をおいて再試行してください。');
			}
			throw cause;
		}
	}
}

// CSRF対策付きの更新API呼び出し。ログイン前後でトークンの対象ユーザーが変わるため、呼び出しの直前に取得する。
export async function csrfRequest(
	path: string,
	method: 'POST' | 'PUT' | 'DELETE',
	body?: unknown,
	options: ApiFetchOptions = {}
): Promise<Response> {
	const csrf = await apiFetch('/api/auth/csrf', {}, options);
	if (!csrf.ok) throw new Error('認証の準備に失敗しました。再試行してください。');
	const { token } = await csrf.json().catch(() => ({ token: undefined }));
	if (!token) throw new Error('認証の準備に失敗しました。再試行してください。');
	return apiFetch(path, {
		method,
		headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
		body: body === undefined ? undefined : JSON.stringify(body)
	}, options);
}

async function post(path: string, body?: unknown): Promise<Response> {
	return csrfRequest(path, 'POST', body);
}

// このプロジェクトは対応ブラウザーを限定していないため、非対応環境ではパスキー関連のUIを出さない。
export function passkeysSupported(): boolean {
	return typeof window !== 'undefined' && !!window.PublicKeyCredential;
}

function toBase64Url(value: ArrayBuffer | null | undefined): string | undefined {
	if (!value) return undefined;
	const bytes = new Uint8Array(value);
	let binary = '';
	for (let i = 0; i < bytes.byteLength; i++) binary += String.fromCharCode(bytes[i]);
	return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

// PublicKeyCredential.toJSON() はブラウザー・一部のパスワードマネージャーによって未実装・不完全な場合があり、
// その場合 JSON.stringify(credential) だけでは clientExtensionResults 等が欠落する。
// サーバーが要求するフィールドを手動でBase64URLへ変換して組み立てる。
function serializeCredential(credential: PublicKeyCredential): string {
	// AuthenticatorAttestationResponse/AuthenticatorAssertionResponse は実行環境によってグローバルに
	// 存在しない場合があるため、instanceof ではなくプロパティの有無(WebAuthn仕様上保証される形)で判定する。
	const response = credential.response as AuthenticatorAttestationResponse | AuthenticatorAssertionResponse;
	const responseJson: Record<string, unknown> = {
		clientDataJSON: toBase64Url(response.clientDataJSON)
	};
	if ('attestationObject' in response) {
		responseJson.attestationObject = toBase64Url(response.attestationObject);
		responseJson.authenticatorData = toBase64Url(response.getAuthenticatorData?.());
		responseJson.publicKey = toBase64Url(response.getPublicKey?.() ?? undefined);
		responseJson.publicKeyAlgorithm = response.getPublicKeyAlgorithm?.();
		responseJson.transports = response.getTransports?.();
	} else {
		responseJson.authenticatorData = toBase64Url(response.authenticatorData);
		responseJson.signature = toBase64Url(response.signature);
		responseJson.userHandle = toBase64Url(response.userHandle);
	}
	return JSON.stringify({
		id: credential.id,
		rawId: toBase64Url(credential.rawId),
		type: credential.type,
		authenticatorAttachment: credential.authenticatorAttachment ?? undefined,
		clientExtensionResults: credential.getClientExtensionResults(),
		response: responseJson
	});
}

export type PasskeyInfo = { id: string; name: string; createdAt: string | null; isBackedUp: boolean };

export const auth = {
	get user() { return user; },
	get status() { return status; },
	get isLoggedIn() { return status === 'authenticated'; },
	hasPermission(actionKey: string, level: PermissionLevel): boolean {
		return (user?.permissions[actionKey] ?? PermissionLevel.None) >= level;
	},
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
	async confirmEmail(userId: string, token: string) {
		const response = await post('/api/auth/confirm-email', { userId, token });
		if (!response.ok) throw await responseError(response, 'メールアドレスを確認できませんでした。');
	},
	async resendConfirmation(email: string) {
		const response = await post('/api/auth/resend-confirmation', { email });
		if (!response.ok) throw await responseError(response, '再送を受け付けられませんでした。');
		return (await response.json()).message as string;
	},
	async requestPasswordReset(email: string) {
		const response = await post('/api/auth/forgot-password', { email });
		if (!response.ok) throw await responseError(response, '再設定メールの送信を受け付けられませんでした。');
		return (await response.json()).message as string;
	},
	async resetPassword(userId: string, token: string, newPassword: string) {
		const response = await post('/api/auth/reset-password', { userId, token, newPassword });
		if (!response.ok) throw await responseError(response, 'パスワードを再設定できませんでした。');
	},
	async registerPasskey(name?: string) {
		const optionsResponse = await post('/api/auth/passkeys/registration-options');
		if (!optionsResponse.ok) throw await responseError(optionsResponse, 'パスキーの登録を準備できませんでした。');
		const optionsJson = await optionsResponse.json();
		const options = PublicKeyCredential.parseCreationOptionsFromJSON(optionsJson);
		let credential: Credential | null;
		try {
			credential = await navigator.credentials.create({ publicKey: options });
		} catch {
			throw new Error('パスキーの作成がキャンセルされたか失敗しました。');
		}
		if (!credential) throw new Error('パスキーを作成できませんでした。');
		const response = await post('/api/auth/passkeys', {
			credentialJson: serializeCredential(credential as PublicKeyCredential),
			name
		});
		if (!response.ok) throw await responseError(response, 'パスキーを登録できませんでした。');
	},
	async listPasskeys(): Promise<PasskeyInfo[]> {
		const response = await apiFetch('/api/auth/passkeys');
		if (!response.ok) throw await responseError(response, 'パスキーの一覧を取得できませんでした。');
		return await response.json();
	},
	async removePasskey(id: string) {
		const response = await csrfRequest(`/api/auth/passkeys/${encodeURIComponent(id)}`, 'DELETE');
		if (!response.ok) throw await responseError(response, 'パスキーを削除できませんでした。');
	},
	async loginWithPasskey(email: string) {
		const optionsResponse = await post('/api/auth/passkeys/login-options', { email });
		if (!optionsResponse.ok) throw await responseError(optionsResponse, 'パスキーログインを準備できませんでした。');
		const optionsJson = await optionsResponse.json();
		const options = PublicKeyCredential.parseRequestOptionsFromJSON(optionsJson);
		let credential: Credential | null;
		try {
			credential = await navigator.credentials.get({ publicKey: options });
		} catch {
			throw new Error('パスキーでの認証がキャンセルされたか失敗しました。');
		}
		if (!credential) throw new Error('パスキーで認証できませんでした。');
		const response = await post('/api/auth/passkeys/login', {
			credentialJson: serializeCredential(credential as PublicKeyCredential)
		});
		if (!response.ok) throw await responseError(response, 'ログインできませんでした。');
		user = await response.json();
		status = 'authenticated';
	},
	async logout() {
		// サーバー側で Cookie は既に削除されているため、通信自体が失敗しても
		// ローカル状態は解除する(例外の有無にかかわらず finally で必ず実行)。
		try {
			const response = await post('/api/auth/logout');
			if (!response.ok) throw await responseError(response, 'ログアウトできませんでした。');
		} finally {
			user = null;
			status = 'anonymous';
		}
	}
};
