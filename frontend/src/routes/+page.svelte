<script lang="ts">
	import { goto } from '$app/navigation';
	import { auth } from '$lib/auth.svelte';

	$effect(() => {
		if (auth.status === 'anonymous') goto('/login');
	});

	let error = $state('');
	let submitting = $state(false);

	async function handleLogout() {
		if (submitting) return;
		submitting = true;
		error = '';
		try {
			await auth.logout();
			await goto('/login');
		} catch {
			error = 'ログアウトできませんでした。接続を確認して再試行してください。';
		} finally {
			submitting = false;
		}
	}
</script>

{#if auth.isLoggedIn}
	<main>
		<div class="card">
			<h1>トップ画面</h1>
			<p>{auth.user?.email} でログイン中です。</p>
			<button onclick={handleLogout} disabled={submitting}>ログアウト</button>
			{#if error}<p role="alert">{error}</p>{/if}
		</div>
	</main>
{/if}

<style>
	main {
		max-width: 480px;
		margin: var(--space-5xl) auto;
		padding: 0 var(--space-xl);
	}

	.card {
		display: flex;
		flex-direction: column;
		align-items: flex-start;
		gap: var(--space-lg);
		background: #fff;
		border-radius: var(--radius-card);
		box-shadow: var(--shadow-md);
		padding: var(--space-2xl);
	}

	p {
		margin: 0;
		color: var(--color-neutral-700);
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

	button:active {
		background: var(--color-primary-700);
	}

	button:focus-visible {
		outline: 2px solid var(--color-primary);
		outline-offset: 2px;
	}
</style>
