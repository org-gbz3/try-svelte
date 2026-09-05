<script lang="ts">
	import { goto } from '$app/navigation';
	import { auth } from '$lib/auth.svelte';

	$effect(() => {
		if (!auth.isLoggedIn) goto('/login');
	});

	function handleLogout() {
		auth.logout();
		goto('/login');
	}
</script>

{#if auth.isLoggedIn}
	<main>
		<h1>トップ画面</h1>
		<p>{auth.user?.email} でログイン中です。</p>
		<button onclick={handleLogout}>ログアウト</button>
	</main>
{/if}

<style>
	main {
		max-width: 480px;
		margin: 4rem auto;
		padding: 0 1rem;
	}

	button {
		padding: 0.6rem 1rem;
		font-size: 1rem;
		cursor: pointer;
	}
</style>
