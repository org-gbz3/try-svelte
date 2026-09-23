<script lang="ts">
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { auth, passkeysSupported, type PasskeyInfo } from '$lib/auth.svelte';

	let passkeys = $state<PasskeyInfo[]>([]);
	let loading = $state(true);
	let loadError = $state('');
	let name = $state('');
	let registering = $state(false);
	let registerError = $state('');
	let removingId = $state('');
	let removeError = $state('');

	$effect(() => {
		if (auth.status === 'anonymous') goto('/login');
	});

	async function load() {
		loading = true;
		loadError = '';
		try {
			passkeys = await auth.listPasskeys();
		} catch (cause) {
			loadError = cause instanceof Error ? cause.message : 'パスキーの一覧を取得できませんでした。';
		} finally {
			loading = false;
		}
	}

	onMount(load);

	async function register(event: SubmitEvent) {
		event.preventDefault();
		if (registering) return;
		registering = true;
		registerError = '';
		try {
			await auth.registerPasskey(name || undefined);
			name = '';
			await load();
		} catch (cause) {
			registerError = cause instanceof Error ? cause.message : 'パスキーを登録できませんでした。';
		} finally {
			registering = false;
		}
	}

	async function remove(id: string) {
		if (removingId) return;
		removingId = id;
		removeError = '';
		try {
			await auth.removePasskey(id);
			await load();
		} catch (cause) {
			removeError = cause instanceof Error ? cause.message : 'パスキーを削除できませんでした。';
		} finally {
			removingId = '';
		}
	}
</script>

<main>
	<div class="card">
		<h1>パスキーの管理</h1>
		<p><a href="/">トップ画面に戻る</a></p>

		{#if !passkeysSupported()}
			<p role="alert">この端末・ブラウザーはパスキーに対応していません。</p>
		{:else}
			<section>
				<h2>パスキーを追加</h2>
				<form onsubmit={register}>
					<label>
						名前(任意)
						<input type="text" bind:value={name} maxlength="64" placeholder="例: 会社のノートPC" />
					</label>
					{#if registerError}<p class="error" role="alert">{registerError}</p>{/if}
					<button type="submit" disabled={registering}>
						{registering ? '登録中…' : 'このデバイスにパスキーを追加'}
					</button>
				</form>
			</section>

			<section>
				<h2>登録済みのパスキー</h2>
				{#if loading}
					<p>読み込み中…</p>
				{:else if loadError}
					<p class="error" role="alert">{loadError}</p>
				{:else if passkeys.length === 0}
					<p>登録済みのパスキーはありません。</p>
				{:else}
					<ul class="passkeys">
						{#each passkeys as passkey (passkey.id)}
							<li>
								<span>{passkey.name}</span>
								<button
									type="button"
									class="remove"
									onclick={() => remove(passkey.id)}
									disabled={removingId === passkey.id}
								>
									{removingId === passkey.id ? '削除中…' : '削除'}
								</button>
							</li>
						{/each}
					</ul>
					{#if removeError}<p class="error" role="alert">{removeError}</p>{/if}
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

	.passkeys {
		display: flex;
		flex-direction: column;
		gap: var(--space-sm);
		margin: 0;
		padding: 0;
		list-style: none;
	}

	.passkeys li {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: var(--space-md);
		padding: var(--space-sm) var(--space-md);
		background: var(--color-neutral-50);
		border-radius: var(--radius-input);
	}

	.remove {
		background: var(--color-danger);
		box-shadow: none;
	}

	.error {
		margin: 0;
		font-size: var(--font-size-body);
		color: var(--color-danger);
	}
</style>
