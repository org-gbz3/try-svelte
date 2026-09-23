<script lang="ts">
	import { onMount } from 'svelte';
	import { auth } from '$lib/auth.svelte';

	let userId = $state('');
	let token = $state('');
	let password = $state('');
	let passwordConfirm = $state('');
	let error = $state('');
	let submitting = $state(false);
	let done = $state(false);

	const passwordRequirements = $derived([
		{ label: '12文字以上', met: password.length >= 12 },
		{ label: '大文字を含む', met: /[A-Z]/.test(password) },
		{ label: '小文字を含む', met: /[a-z]/.test(password) },
		{ label: '数字を含む', met: /[0-9]/.test(password) },
		{ label: '記号を含む', met: /[^A-Za-z0-9]/.test(password) }
	]);

	onMount(() => {
		const params = new URLSearchParams(window.location.hash.slice(1));
		userId = params.get('userId') ?? '';
		token = params.get('token') ?? '';
		if (!userId || !token) error = '再設定リンクが不正です。再設定をやり直してください。';
	});

	async function handleSubmit(event: SubmitEvent) {
		event.preventDefault();
		if (submitting) return;
		if (!password || !passwordConfirm) {
			error = 'すべての項目を入力してください';
			return;
		}
		if (password !== passwordConfirm) {
			error = 'パスワードが一致しません';
			return;
		}
		error = '';
		submitting = true;
		try {
			await auth.resetPassword(userId, token, password);
			done = true;
			window.history.replaceState(window.history.state, '', window.location.pathname);
			token = '';
			password = '';
			passwordConfirm = '';
		} catch (cause) {
			error = cause instanceof TypeError ? '通信に失敗しました。接続を確認してください。'
				: cause instanceof Error ? cause.message : '再設定に失敗しました。';
		} finally {
			submitting = false;
		}
	}
</script>

<svelte:head><title>パスワードの再設定</title><meta name="referrer" content="no-referrer" /></svelte:head>

<main>
	<div class="card">
		<h1>パスワードの再設定</h1>
		{#if done}
			<p role="status">パスワードを再設定しました。新しいパスワードでログインしてください。</p>
			<p class="switch"><a href="/login">ログインへ</a></p>
		{:else}
			<form onsubmit={handleSubmit}>
				<label>
					新しいパスワード
					<input
						type="password"
						autocomplete="new-password"
						bind:value={password}
						minlength="12"
						maxlength="128"
						required
						aria-describedby="password-requirements"
					/>
				</label>
				<ul id="password-requirements" class="requirements">
					{#each passwordRequirements as requirement (requirement.label)}
						<li class:met={requirement.met}>
							<span aria-hidden="true">{requirement.met ? '✓' : '○'}</span>
							{requirement.label}
						</li>
					{/each}
				</ul>
				<label>
					新しいパスワード（確認）
					<input
						type="password"
						autocomplete="new-password"
						bind:value={passwordConfirm}
						minlength="12"
						maxlength="128"
						required
					/>
				</label>
				{#if error}
					<p class="error" role="alert">{error}</p>
				{/if}
				<button type="submit" disabled={submitting || !userId || !token}>
					{submitting ? '再設定中…' : 'パスワードを再設定'}
				</button>
			</form>
			<p class="switch"><a href="/login">ログイン・確認メールの再送</a></p>
		{/if}
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

	.requirements {
		display: grid;
		grid-template-columns: repeat(2, 1fr);
		gap: var(--space-2xs) var(--space-md);
		margin: calc(-1 * var(--space-xs)) 0 0;
		padding: 0;
		list-style: none;
		font-size: var(--font-size-caption);
		color: var(--color-neutral-500);
	}

	.requirements li {
		display: flex;
		align-items: center;
		gap: var(--space-2xs);
	}

	.requirements li.met {
		color: var(--color-success);
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
