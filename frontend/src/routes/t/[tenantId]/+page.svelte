<script lang="ts">
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import { apiFetch, auth, PermissionLevel } from '$lib/auth.svelte';

	type TenantMe = { id: string; name: string; permissions: Record<string, PermissionLevel> };

	const tenantId = $derived(page.params.tenantId);

	let tenant = $state<TenantMe | null>(null);
	let loading = $state(true);
	let forbidden = $state(false);
	let loadError = $state('');

	$effect(() => {
		if (auth.status === 'anonymous') goto('/login');
	});

	// テナント内の権限はテナントごとに異なるため、システム権限(auth.user.permissions)とは別にこの画面で取得する。
	function can(actionKey: string, level: PermissionLevel): boolean {
		return (tenant?.permissions[actionKey] ?? PermissionLevel.None) >= level;
	}

	async function load(id: string) {
		loading = true;
		loadError = '';
		forbidden = false;
		tenant = null;
		try {
			const response = await apiFetch(`/api/tenants/${id}/me`);
			if (response.status === 403) {
				forbidden = true;
				return;
			}
			if (!response.ok) throw new Error('テナント情報を取得できませんでした。');
			tenant = await response.json();
		} catch (cause) {
			loadError = cause instanceof TypeError
				? '通信に失敗しました。接続を確認してください。'
				: cause instanceof Error ? cause.message : '読み込みに失敗しました。';
		} finally {
			loading = false;
		}
	}

	// 所属テナント間をリンクで移動したときも、同じコンポーネントのまま対象テナントを読み直す。
	$effect(() => {
		if (tenantId) void load(tenantId);
	});
</script>

{#if auth.isLoggedIn}
	<main>
		<p class="back"><a href="/">トップ画面に戻る</a></p>

		{#if loading}
			<p role="status">読み込み中です…</p>
		{:else if forbidden}
			<p class="card" role="alert">このテナントに所属していないため、表示できません。</p>
		{:else if loadError}
			<div class="card">
				<p role="alert">{loadError}</p>
				<button type="button" onclick={() => tenantId && load(tenantId)}>再試行</button>
			</div>
		{:else if tenant}
			<div class="card">
				<h1>{tenant.name}</h1>
				{#if can('Tenant.Roles', PermissionLevel.Read)}
					<a href={`/t/${tenant.id}/settings/roles`}>テナントロール管理</a>
				{/if}
				{#if can('Tenant.MemberRoles', PermissionLevel.Read)}
					<a href={`/t/${tenant.id}/settings/members`}>メンバーのロール割り当て</a>
				{/if}
				{#if !can('Tenant.Roles', PermissionLevel.Read) && !can('Tenant.MemberRoles', PermissionLevel.Read)}
					<p>利用できる機能はまだありません。</p>
				{/if}
			</div>
		{/if}
	</main>
{/if}

<style>
	main {
		max-width: 480px;
		margin: var(--space-5xl) auto;
		padding: 0 var(--space-xl);
		display: flex;
		flex-direction: column;
		gap: var(--space-lg);
	}

	.back a {
		font-size: var(--font-size-caption);
	}

	a {
		color: var(--color-primary);
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

	h1 {
		margin: 0;
		overflow-wrap: anywhere;
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

	button:focus-visible {
		outline: 2px solid var(--color-primary);
		outline-offset: 2px;
	}
</style>
