<script lang="ts">
	import '../app.css';
	import { onMount } from 'svelte';
	import { auth } from '$lib/auth.svelte';
	import favicon from '$lib/assets/favicon.svg';

	let { children } = $props();
	onMount(() => { void auth.check(); });
</script>

<svelte:head>
	<link rel="icon" href={favicon} />
</svelte:head>

{#if auth.status === 'checking'}
	<p role="status">ログイン状態を確認中です…</p>
{:else if auth.status === 'error'}
	<p role="alert">ログイン状態を確認できませんでした。接続を確認して再試行してください。</p>
	<button onclick={() => auth.check()}>再試行</button>
{:else}
	{@render children()}
{/if}
