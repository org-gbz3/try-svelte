<script lang="ts">
	import { goto } from '$app/navigation';
	import { auth } from '$lib/auth.svelte';

	let email = $state('');
	let password = $state('');
	let error = $state('');

	$effect(() => {
		if (auth.isLoggedIn) goto('/');
	});

	function handleSubmit(event: SubmitEvent) {
		event.preventDefault();
		if (!email || !password) {
			error = 'メールアドレスとパスワードを入力してください';
			return;
		}
		error = '';
		auth.login(email);
		goto('/');
	}
</script>

<main>
	<h1>ログイン</h1>
	<form onsubmit={handleSubmit}>
		<label>
			メールアドレス
			<input type="email" autocomplete="email" bind:value={email} required />
		</label>
		<label>
			パスワード
			<input type="password" autocomplete="current-password" bind:value={password} required />
		</label>
		{#if error}
			<p class="error">{error}</p>
		{/if}
		<button type="submit">ログイン</button>
	</form>
	<p class="switch">アカウントをお持ちでない方は <a href="/signup">アカウント作成</a></p>
</main>

<style>
	main {
		max-width: 320px;
		margin: 4rem auto;
		padding: 0 1rem;
	}

	form {
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
	}

	label {
		display: flex;
		flex-direction: column;
		gap: 0.25rem;
		font-size: 0.9rem;
	}

	input {
		padding: 0.5rem;
		font-size: 1rem;
	}

	button {
		padding: 0.6rem;
		font-size: 1rem;
		cursor: pointer;
	}

	.error {
		color: #c0392b;
		margin: 0;
		font-size: 0.9rem;
	}

	.switch {
		margin-top: 1rem;
		font-size: 0.9rem;
	}
</style>
