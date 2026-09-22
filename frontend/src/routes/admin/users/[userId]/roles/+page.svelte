<script lang="ts">
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import { apiFetch, auth, csrfRequest, responseError } from '$lib/auth.svelte';

	type RoleResponse = { id: string; name: string };
	type UserRolesResponse = { userId: string; roles: string[] };

	const userId = $derived(page.params.userId);
	const email = $derived(page.url.searchParams.get('email') ?? '');

	let allRoles = $state<RoleResponse[]>([]);
	let editedRoles = $state<string[]>([]);
	let loading = $state(true);
	let forbidden = $state(false);
	let notFound = $state(false);
	let loadError = $state('');
	let saving = $state(false);
	let saveError = $state('');
	let saved = $state(false);

	$effect(() => {
		if (auth.status === 'anonymous') goto('/login');
	});

	async function load() {
		loading = true;
		loadError = '';
		forbidden = false;
		notFound = false;
		saved = false;
		try {
			const [rolesResponse, userRolesResponse] = await Promise.all([
				apiFetch('/api/admin/roles'),
				apiFetch(`/api/admin/users/${userId}/roles`)
			]);
			if (rolesResponse.status === 403 || userRolesResponse.status === 403) {
				forbidden = true;
				return;
			}
			if (userRolesResponse.status === 404) {
				notFound = true;
				return;
			}
			if (!rolesResponse.ok || !userRolesResponse.ok) throw new Error('ロール情報を取得できませんでした。');
			allRoles = await rolesResponse.json();
			const userRoles: UserRolesResponse = await userRolesResponse.json();
			editedRoles = [...userRoles.roles];
		} catch (cause) {
			loadError = cause instanceof TypeError
				? '通信に失敗しました。接続を確認してください。'
				: cause instanceof Error ? cause.message : '読み込みに失敗しました。';
		} finally {
			loading = false;
		}
	}

	onMount(() => { void load(); });

	function toggleRole(roleName: string) {
		editedRoles = editedRoles.includes(roleName)
			? editedRoles.filter((name) => name !== roleName)
			: [...editedRoles, roleName];
	}

	async function save(event: SubmitEvent) {
		event.preventDefault();
		if (saving) return;
		saving = true;
		saveError = '';
		saved = false;
		try {
			const response = await csrfRequest(`/api/admin/users/${userId}/roles`, 'PUT', { roles: editedRoles });
			if (!response.ok) throw await responseError(response, 'ロールを保存できませんでした。');
			await load();
			saved = true;
		} catch (cause) {
			saveError = cause instanceof TypeError
				? '通信に失敗しました。接続を確認してください。'
				: cause instanceof Error ? cause.message : '保存に失敗しました。';
		} finally {
			saving = false;
		}
	}
</script>

{#if auth.isLoggedIn}
	<main>
		<h1>ロールの編集</h1>
		<p class="back"><a href="/admin/users">ユーザー一覧に戻る</a></p>

		{#if loading}
			<p role="status">読み込み中です…</p>
		{:else if forbidden}
			<p class="card" role="alert">この画面を利用する権限がありません。</p>
		{:else if notFound}
			<p class="card" role="alert">このユーザーは見つかりません。</p>
		{:else if loadError}
			<div class="card">
				<p role="alert">{loadError}</p>
				<button type="button" onclick={() => load()}>再試行</button>
			</div>
		{:else}
			<section class="card">
				<p class="target">{email || userId}</p>
				<form onsubmit={save}>
					<ul class="role-list">
						{#each allRoles as role (role.id)}
							<li>
								<label>
									<input
										type="checkbox"
										checked={editedRoles.includes(role.name)}
										onchange={() => toggleRole(role.name)}
									/>
									{role.name}
								</label>
							</li>
						{:else}
							<li class="empty">ロールがまだありません。</li>
						{/each}
					</ul>
					{#if saveError}<p class="error" role="alert">{saveError}</p>{/if}
					{#if saved}<p role="status">保存しました。</p>{/if}
					<button type="submit" disabled={saving}>保存</button>
				</form>
			</section>
		{/if}
	</main>
{/if}

<style>
	main {
		max-width: 640px;
		margin: var(--space-5xl) auto;
		padding: 0 var(--space-xl);
		display: flex;
		flex-direction: column;
		gap: var(--space-lg);
	}

	.back a {
		color: var(--color-primary);
		font-size: var(--font-size-caption);
	}

	.card {
		background: #fff;
		border-radius: var(--radius-card);
		box-shadow: var(--shadow-md);
		padding: var(--space-2xl);
	}

	.target {
		margin: 0 0 var(--space-lg);
		font-weight: var(--font-weight-heading);
		color: var(--color-neutral-900);
	}

	form {
		display: flex;
		flex-direction: column;
		gap: var(--space-md);
	}

	.role-list {
		list-style: none;
		margin: 0;
		padding: 0;
		display: flex;
		flex-direction: column;
		gap: var(--space-sm);
	}

	.role-list label {
		display: flex;
		align-items: center;
		gap: var(--space-sm);
		font-size: var(--font-size-body);
		color: var(--color-neutral-900);
	}

	.role-list .empty {
		color: var(--color-neutral-500);
		font-size: var(--font-size-caption);
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
