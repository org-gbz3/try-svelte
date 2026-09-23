<script lang="ts">
	import { page } from '$app/state';
	import { goto } from '$app/navigation';
	import { auth, passkeysSupported } from '$lib/auth.svelte';

	let email = $state('');
	let password = $state('');
	let error = $state('');
	let submitting = $state(false);
	let notice = $state('');

	async function resend() {
		if (submitting) return;
		if (!email) { error = 'メールアドレスを入力してください'; return; }
		submitting = true;
		error = '';
		notice = '';
		try { notice = await auth.resendConfirmation(email); }
		catch (cause) { error = cause instanceof Error ? cause.message : '再送に失敗しました。'; }
		finally { submitting = false; }
	}

	async function loginWithPasskey() {
		if (submitting) return;
		if (!email) { error = 'メールアドレスを入力してください'; return; }
		error = '';
		submitting = true;
		try {
			await auth.loginWithPasskey(email);
			await goto('/');
		} catch (cause) {
			error = cause instanceof TypeError ? '通信に失敗しました。接続を確認してください。'
				: cause instanceof Error ? cause.message : '処理に失敗しました。';
		} finally {
			submitting = false;
		}
	}

	$effect(() => {
		if (auth.isLoggedIn) goto('/');
	});

	async function handleSubmit(event: SubmitEvent) {
		event.preventDefault();
		if (submitting) return;
		if (!email || !password) {
			error = 'メールアドレスとパスワードを入力してください';
			return;
		}
		error = '';
		submitting = true;
		try {
			await auth.login(email, password);
			await goto("/");
		} catch (cause) {
			error = cause instanceof TypeError ? '通信に失敗しました。接続を確認してください。'
				: cause instanceof Error ? cause.message : '処理に失敗しました。';
		} finally {
			submitting = false;
		}
	}
</script>

<main>
	<div class="card">
		<h1>ログイン</h1>
		{#if page.url.searchParams.get("registered") === "1"}
			<p role="status">確認メールを送信しました。メール内のリンクで確認を完了してからログインしてください。</p>
		{/if}
		<p>メールアドレスの確認が完了するとログインできます。</p>
		{#if notice}<p role="status">{notice}</p>{/if}
		<form onsubmit={handleSubmit}>
			<label>
				メールアドレス
				<input type="email" autocomplete="email" bind:value={email} maxlength="254" required />
			</label>
			<label>
				パスワード
				<input type="password" autocomplete="current-password" bind:value={password} maxlength="128" required />
			</label>
			{#if error}
				<p class="error" role="alert">{error}</p>
			{/if}
			<button type="submit" disabled={submitting}>ログイン</button>
			{#if passkeysSupported()}
				<button type="button" onclick={loginWithPasskey} disabled={submitting}>パスキーでログイン</button>
			{/if}
			<button type="button" onclick={resend} disabled={submitting}>確認メールを再送</button>
		</form>
		<p class="switch">アカウントをお持ちでない方は <a href="/signup">アカウント作成</a></p>
		<p class="switch"><a href="/forgot-password">パスワードをお忘れですか?</a></p>
	</div>
</main>

<style>
	main {
		max-width: 400px;
		margin: var(--space-5xl) auto;
		padding: 0 var(--space-xl);
	}

	.card {
		background: #fff;
		border-radius: var(--radius-card);
		box-shadow: var(--shadow-md);
		padding: var(--space-2xl);
	}

	form {
		display: flex;
		flex-direction: column;
		gap: var(--space-md);
		margin-top: var(--space-xl);
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
		margin-top: var(--space-sm);
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

	button:active {
		background: var(--color-primary-700);
	}

	button:focus-visible {
		outline: 2px solid var(--color-primary);
		outline-offset: 2px;
	}

	button:disabled {
		opacity: 0.4;
		cursor: not-allowed;
	}

	.error {
		margin: 0;
		font-size: var(--font-size-body);
		color: var(--color-danger);
	}

	.switch {
		margin: var(--space-xl) 0 0;
		font-size: var(--font-size-caption);
		color: var(--color-neutral-600);
	}

	.switch a {
		color: var(--color-primary);
	}
</style>
