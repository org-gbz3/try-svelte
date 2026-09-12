<script lang="ts">
	import { onMount } from 'svelte';
	import { auth } from '$lib/auth.svelte';

	let userId = $state('');
	let token = $state('');
	let error = $state('');
	let submitting = $state(false);
	let confirmed = $state(false);

	onMount(() => {
		const params = new URLSearchParams(window.location.hash.slice(1));
		userId = params.get('userId') ?? '';
		token = params.get('token') ?? '';
		if (!userId || !token) error = '確認リンクが不正です。確認メールを再送してください。';
	});

	async function confirm() {
		if (submitting) return;
		submitting = true;
		error = '';
		try {
			await auth.confirmEmail(userId, token);
			confirmed = true;
			window.history.replaceState(window.history.state, '', window.location.pathname);
			token = '';
		} catch (cause) {
			error = cause instanceof Error ? cause.message : '確認に失敗しました。再試行してください。';
		} finally {
			submitting = false;
		}
	}
</script>

<svelte:head><title>メールアドレスの確認</title><meta name="referrer" content="no-referrer" /></svelte:head>

<main>
	<h1>メールアドレスの確認</h1>
	{#if confirmed}
		<p role="status">メールアドレスを確認しました。ログインできます。</p>
	{:else}
		<p>ボタンを押すとメールアドレスの確認が完了します。</p>
		<button onclick={confirm} disabled={submitting || !userId || !token}>
			{submitting ? '確認中…' : 'メールアドレスを確認'}
		</button>
	{/if}
	{#if error}<p role="alert">{error}</p>{/if}
	<p><a href="/login">ログイン・確認メールの再送</a></p>
</main>

<style>
	main { max-width: 480px; margin: var(--space-5xl) auto; padding: var(--space-2xl); background: #fff; border-radius: var(--radius-card); box-shadow: var(--shadow-md); }
	button { padding: var(--space-md) var(--space-lg); background: var(--color-primary); color: #fff; border: none; border-radius: var(--radius-button); cursor: pointer; }
	button:disabled { opacity: 0.4; cursor: not-allowed; }
	button:focus-visible { outline: 2px solid var(--color-primary); outline-offset: 2px; }
</style>
