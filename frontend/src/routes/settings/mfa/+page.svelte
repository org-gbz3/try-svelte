<script lang="ts">
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import QRCode from 'qrcode';
	import { auth } from '$lib/auth.svelte';

	let loading = $state(true);
	let loadError = $state('');
	let enabled = $state(false);
	let recoveryCodesRemaining = $state(0);
	let hasPasskey = $state(false);

	// セットアップ中の状態(有効化前)。
	let settingUp = $state(false);
	let otpauthUri = $state('');
	let sharedKey = $state('');
	let qrDataUrl = $state('');
	let code = $state('');
	let enabling = $state(false);
	let setupError = $state('');

	// 有効化・再生成直後に一度だけ表示するリカバリーコード。
	let revealedRecoveryCodes = $state<string[] | null>(null);

	// 無効化・再生成にはパスワードでの再認証が必要。
	let confirmingAction = $state<'disable' | 'regenerate' | null>(null);
	let reauthPassword = $state('');
	let reauthSubmitting = $state(false);
	let reauthError = $state('');

	$effect(() => {
		if (auth.status === 'anonymous') goto('/login');
	});

	async function load() {
		loading = true;
		loadError = '';
		try {
			const status = await auth.mfaStatus();
			enabled = status.enabled;
			recoveryCodesRemaining = status.recoveryCodesRemaining;
			hasPasskey = (await auth.listPasskeys()).length > 0;
		} catch (cause) {
			loadError = cause instanceof Error ? cause.message : 'MFAの状態を取得できませんでした。';
		} finally {
			loading = false;
		}
	}

	onMount(load);

	async function startSetup() {
		setupError = '';
		try {
			const setup = await auth.setupMfa();
			otpauthUri = setup.otpauthUri;
			sharedKey = setup.sharedKey;
			qrDataUrl = await QRCode.toDataURL(setup.otpauthUri);
			settingUp = true;
		} catch (cause) {
			setupError = cause instanceof Error ? cause.message : 'MFAの設定を開始できませんでした。';
		}
	}

	async function confirmEnable(event: SubmitEvent) {
		event.preventDefault();
		if (enabling || !code) return;
		enabling = true;
		setupError = '';
		try {
			revealedRecoveryCodes = await auth.enableMfa(code);
			settingUp = false;
			code = '';
			await load();
		} catch (cause) {
			setupError = cause instanceof Error ? cause.message : 'MFAを有効化できませんでした。';
		} finally {
			enabling = false;
		}
	}

	function startReauth(action: 'disable' | 'regenerate') {
		confirmingAction = action;
		reauthPassword = '';
		reauthError = '';
	}

	async function submitReauth(event: SubmitEvent) {
		event.preventDefault();
		if (reauthSubmitting || !reauthPassword) return;
		reauthSubmitting = true;
		reauthError = '';
		try {
			if (confirmingAction === 'disable') {
				await auth.disableMfa(reauthPassword);
				revealedRecoveryCodes = null;
			} else if (confirmingAction === 'regenerate') {
				revealedRecoveryCodes = await auth.regenerateRecoveryCodes(reauthPassword);
			}
			confirmingAction = null;
			reauthPassword = '';
			await load();
		} catch (cause) {
			reauthError = cause instanceof Error ? cause.message : '処理に失敗しました。';
		} finally {
			reauthSubmitting = false;
		}
	}
</script>

<main>
	<div class="card">
		<h1>二段階認証(MFA)の管理</h1>
		<p><a href="/">トップ画面に戻る</a></p>

		{#if loading}
			<p>読み込み中…</p>
		{:else if loadError}
			<p class="error" role="alert">{loadError}</p>
		{:else if revealedRecoveryCodes}
			<section>
				<h2>リカバリーコード</h2>
				<p role="alert">この画面を離れると二度と表示されません。安全な場所に保存してください。</p>
				<ul class="codes">
					{#each revealedRecoveryCodes as recoveryCode}
						<li>{recoveryCode}</li>
					{/each}
				</ul>
				<button type="button" onclick={() => { revealedRecoveryCodes = null; }}>保存しました</button>
			</section>
		{:else if !enabled}
			<section>
				{#if !settingUp}
					<p>MFAは無効です。認証アプリ(Google Authenticator など)を使って有効化できます。</p>
					{#if setupError}<p class="error" role="alert">{setupError}</p>{/if}
					<button type="button" onclick={startSetup}>MFAを有効にする</button>
				{:else}
					<h2>認証アプリで読み取る</h2>
					{#if qrDataUrl}<img src={qrDataUrl} alt="MFA設定用QRコード" width="200" height="200" />{/if}
					<p>QRコードを読み取れない場合は、次のキーを手動で入力してください。</p>
					<p class="key">{sharedKey}</p>
					<form onsubmit={confirmEnable}>
						<label>
							確認コード
							<input type="text" inputmode="numeric" autocomplete="one-time-code" bind:value={code} maxlength="6" required />
						</label>
						{#if setupError}<p class="error" role="alert">{setupError}</p>{/if}
						<button type="submit" disabled={enabling}>確認して有効化</button>
					</form>
				{/if}
			</section>
		{:else}
			<section>
				<p>MFAは有効です。残りのリカバリーコード: {recoveryCodesRemaining}件</p>
				{#if hasPasskey}
					<p>パスキーでログインする場合、このコードの入力は不要です。</p>
				{/if}
				{#if confirmingAction}
					<form onsubmit={submitReauth}>
						<label>
							現在のパスワード
							<input type="password" autocomplete="current-password" bind:value={reauthPassword} maxlength="128" required />
						</label>
						{#if reauthError}<p class="error" role="alert">{reauthError}</p>{/if}
						<button type="submit" disabled={reauthSubmitting}>
							{confirmingAction === 'disable' ? '無効化を確定' : 'リカバリーコードを再生成'}
						</button>
						<button type="button" class="secondary" onclick={() => { confirmingAction = null; }}>キャンセル</button>
					</form>
				{:else}
					<button type="button" onclick={() => startReauth('regenerate')}>リカバリーコードを再生成</button>
					<button type="button" class="danger" onclick={() => startReauth('disable')}>MFAを無効にする</button>
				{/if}
			</section>
		{/if}
	</div>
</main>

<style>
	main {
		max-width: 480px;
		margin: var(--space-5xl) auto;
		padding: 0 var(--space-xl);
	}

	.card {
		display: flex;
		flex-direction: column;
		gap: var(--space-lg);
		background: #fff;
		border-radius: var(--radius-card);
		box-shadow: var(--shadow-md);
		padding: var(--space-2xl);
	}

	section {
		display: flex;
		flex-direction: column;
		gap: var(--space-sm);
	}

	form {
		display: flex;
		flex-direction: column;
		gap: var(--space-md);
	}

	label {
		display: flex;
		flex-direction: column;
		gap: var(--space-xs);
		font-size: var(--font-size-body);
		color: var(--color-neutral-700);
	}

	input {
		padding: var(--space-md) var(--space-lg);
		font-family: var(--font-family-base);
		font-size: var(--font-size-body);
		color: var(--color-neutral-900);
		background: var(--color-neutral-50);
		border: 1px solid var(--color-neutral-300);
		border-radius: var(--radius-input);
	}

	input:focus-visible {
		outline: 2px solid var(--color-primary);
		outline-offset: 2px;
		border-color: var(--color-primary);
	}

	button {
		padding: var(--space-md) var(--space-lg);
		font-family: var(--font-family-base);
		font-size: var(--font-size-body);
		font-weight: var(--font-weight-heading);
		color: #fff;
		background: var(--color-primary);
		border: none;
		border-radius: var(--radius-button);
		box-shadow: var(--shadow-sm);
		cursor: pointer;
	}

	button:hover {
		background: var(--color-primary-600);
	}

	button:focus-visible {
		outline: 2px solid var(--color-primary);
		outline-offset: 2px;
	}

	button:disabled {
		opacity: 0.4;
		cursor: not-allowed;
	}

	button.secondary {
		color: var(--color-neutral-900);
		background: var(--color-neutral-100);
		box-shadow: none;
	}

	button.danger {
		background: var(--color-danger);
		box-shadow: none;
	}

	.key {
		font-family: monospace;
		font-size: var(--font-size-body);
		padding: var(--space-sm) var(--space-md);
		background: var(--color-neutral-50);
		border-radius: var(--radius-input);
		word-break: break-all;
	}

	.codes {
		display: grid;
		grid-template-columns: repeat(2, 1fr);
		gap: var(--space-sm);
		margin: 0;
		padding: var(--space-md);
		background: var(--color-neutral-50);
		border-radius: var(--radius-input);
		font-family: monospace;
		list-style: none;
	}

	.error {
		margin: 0;
		font-size: var(--font-size-body);
		color: var(--color-danger);
	}
</style>
