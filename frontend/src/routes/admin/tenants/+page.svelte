<script lang="ts">
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { apiFetch, auth, csrfRequest, responseError, PermissionLevel } from '$lib/auth.svelte';

	type TenantListItem = { id: string; name: string; createdAt: string; memberCount: number };

	let tenants = $state<TenantListItem[]>([]);
	let loading = $state(true);
	let forbidden = $state(false);
	let loadError = $state('');

	let newTenantName = $state('');
	let creating = $state(false);
	let createError = $state('');

	// 作成フォームは Write 権限がある場合のみ表示する(APIでも拒否されるが、押せないボタンを出さないため)。
	const canWrite = $derived(auth.hasPermission('Admin.Tenants', PermissionLevel.Write));

	$effect(() => {
		if (auth.status === 'anonymous') goto('/login');
	});

	async function load() {
		loading = true;
		loadError = '';
		forbidden = false;
		try {
			const response = await apiFetch('/api/admin/tenants');
			if (response.status === 403) {
				forbidden = true;
				return;
			}
			if (!response.ok) throw new Error('テナント一覧を取得できませんでした。');
			tenants = await response.json();
		} catch (cause) {
			loadError = cause instanceof TypeError
				? '通信に失敗しました。接続を確認してください。'
				: cause instanceof Error ? cause.message : '読み込みに失敗しました。';
		} finally {
			loading = false;
		}
	}

	onMount(() => { void load(); });

	async function createTenant(event: SubmitEvent) {
		event.preventDefault();
		if (creating) return;
		const name = newTenantName.trim();
		if (!name) { createError = 'テナント名を入力してください。'; return; }
		creating = true;
		createError = '';
		try {
			const response = await csrfRequest('/api/admin/tenants', 'POST', { name });
			if (!response.ok) throw await responseError(response, 'テナントを作成できませんでした。');
			newTenantName = '';
			await load();
		} catch (cause) {
			createError = cause instanceof TypeError
				? '通信に失敗しました。接続を確認してください。'
				: cause instanceof Error ? cause.message : '作成に失敗しました。';
		} finally {
			creating = false;
		}
	}
</script>

{#if auth.isLoggedIn}
	<main>
		<h1>テナント管理</h1>
		<p class="back"><a href="/">トップ画面に戻る</a></p>

		{#if loading}
			<p role="status">読み込み中です…</p>
		{:else if forbidden}
			<p class="card" role="alert">この画面を利用する権限がありません。</p>
		{:else if loadError}
			<div class="card">
				<p role="alert">{loadError}</p>
				<button type="button" onclick={() => load()}>再試行</button>
			</div>
		{:else}
			<section class="card">
				<table>
					<thead>
						<tr>
							<th scope="col">テナント名</th>
							<th scope="col">所属人数</th>
						</tr>
					</thead>
					<tbody>
						{#each tenants as tenant (tenant.id)}
							<tr>
								<td><a href={`/admin/tenants/${tenant.id}`}>{tenant.name}</a></td>
								<td>{tenant.memberCount}</td>
							</tr>
						{:else}
							<tr><td colspan="2" class="empty">テナントがまだありません。</td></tr>
						{/each}
					</tbody>
				</table>

				{#if canWrite}
					<form onsubmit={createTenant}>
						<label>
							新規テナント名
							<input bind:value={newTenantName} maxlength="256" required />
						</label>
						{#if createError}<p class="error" role="alert">{createError}</p>{/if}
						<button type="submit" disabled={creating}>作成</button>
					</form>
				{/if}
			</section>
		{/if}
	</main>
{/if}

<style>
	main {
		max-width: 720px;
		margin: var(--space-5xl) auto;
		padding: 0 var(--space-xl);
		display: flex;
		flex-direction: column;
		gap: var(--space-lg);
	}

	.back a,
	td a {
		color: var(--color-primary);
	}

	.back a {
		font-size: var(--font-size-caption);
	}

	.card {
		background: #fff;
		border-radius: var(--radius-card);
		box-shadow: var(--shadow-md);
		padding: var(--space-2xl);
	}

	table {
		width: 100%;
		border-collapse: collapse;
		margin-bottom: var(--space-xl);
	}

	th,
	td {
		text-align: left;
		padding: var(--space-sm) var(--space-md);
		border-bottom: 1px solid var(--color-neutral-200);
		font-size: var(--font-size-body);
	}

	.empty {
		color: var(--color-neutral-500);
		font-size: var(--font-size-caption);
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

	button {
		align-self: flex-start;
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
</style>
