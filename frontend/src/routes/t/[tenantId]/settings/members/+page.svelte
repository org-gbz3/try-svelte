<script lang="ts">
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import { apiFetch, auth, csrfRequest, responseError } from '$lib/auth.svelte';

	type TenantRole = { id: string; name: string };
	type TenantMember = { userId: string; email: string; roleIds: string[] };
	type MembersResponse = { roles: TenantRole[]; members: TenantMember[] };

	const tenantId = $derived(page.params.tenantId);

	let roles = $state<TenantRole[]>([]);
	let members = $state<TenantMember[]>([]);
	let loading = $state(true);
	let forbidden = $state(false);
	let loadError = $state('');

	// メンバーごとの編集中のロール割り当て。保存までサーバーの状態とは独立して保持する。
	let editedRoles = $state<Record<string, string[]>>({});
	let busyMemberId = $state<string | null>(null);
	let memberError = $state<Record<string, string>>({});
	let savedMemberId = $state<string | null>(null);

	$effect(() => {
		if (auth.status === 'anonymous') goto('/login');
	});

	function errorMessage(cause: unknown, fallback: string): string {
		return cause instanceof TypeError
			? '通信に失敗しました。接続を確認してください。'
			: cause instanceof Error ? cause.message : fallback;
	}

	async function load() {
		loading = true;
		loadError = '';
		forbidden = false;
		try {
			const response = await apiFetch(`/api/tenants/${tenantId}/members`);
			if (response.status === 403) {
				forbidden = true;
				return;
			}
			if (!response.ok) throw new Error('メンバー情報を取得できませんでした。');
			const body: MembersResponse = await response.json();
			roles = body.roles;
			members = body.members;
			editedRoles = Object.fromEntries(body.members.map((member) => [member.userId, [...member.roleIds]]));
		} catch (cause) {
			loadError = errorMessage(cause, '読み込みに失敗しました。');
		} finally {
			loading = false;
		}
	}

	$effect(() => {
		if (tenantId) void load();
	});

	function toggleRole(userId: string, roleId: string) {
		const current = editedRoles[userId] ?? [];
		editedRoles = {
			...editedRoles,
			[userId]: current.includes(roleId) ? current.filter((id) => id !== roleId) : [...current, roleId]
		};
	}

	async function saveMemberRoles(member: TenantMember) {
		if (busyMemberId) return;
		busyMemberId = member.userId;
		memberError = { ...memberError, [member.userId]: '' };
		savedMemberId = null;
		try {
			const response = await csrfRequest(
				`/api/tenants/${tenantId}/members/${member.userId}/roles`,
				'PUT',
				{ roleIds: editedRoles[member.userId] ?? [] }
			);
			if (response.status === 403) throw new Error('ロールを変更する権限がありません。');
			if (!response.ok) throw await responseError(response, 'ロールを保存できませんでした。');
			await load();
			savedMemberId = member.userId;
		} catch (cause) {
			memberError = { ...memberError, [member.userId]: errorMessage(cause, '保存に失敗しました。') };
		} finally {
			busyMemberId = null;
		}
	}
</script>

{#if auth.isLoggedIn}
	<main>
		<h1>メンバーのロール割り当て</h1>
		<p class="back"><a href={`/t/${tenantId}`}>テナントのトップに戻る</a></p>

		{#if loading && members.length === 0}
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
				<p class="note">メンバーの追加・所属解除は運営者が行います。</p>
				<ul class="member-list">
					{#each members as member (member.userId)}
						<li class="member">
							<p class="email">{member.email}</p>
							<div class="roles">
								{#each roles as role (role.id)}
									<label class="checkbox">
										<input
											type="checkbox"
											aria-label={`${member.email}: ${role.name}`}
											checked={(editedRoles[member.userId] ?? []).includes(role.id)}
											onchange={() => toggleRole(member.userId, role.id)}
										/>
										{role.name}
									</label>
								{/each}
							</div>
							<button
								type="button"
								aria-label={`${member.email} のロールを保存`}
								disabled={busyMemberId !== null}
								onclick={() => saveMemberRoles(member)}
							>
								ロールを保存
							</button>
							{#if memberError[member.userId]}<p class="error" role="alert">{memberError[member.userId]}</p>{/if}
							{#if savedMemberId === member.userId}<p role="status">保存しました。</p>{/if}
						</li>
					{:else}
						<li class="empty">メンバーがまだいません。</li>
					{/each}
				</ul>
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

	.note {
		margin: 0;
		font-size: var(--font-size-caption);
		color: var(--color-neutral-700);
	}

	.member-list {
		list-style: none;
		margin: 0;
		padding: 0;
		display: flex;
		flex-direction: column;
	}

	.member {
		display: flex;
		flex-direction: column;
		gap: var(--space-sm);
		padding: var(--space-lg) 0;
		border-bottom: 1px solid var(--color-neutral-200);
	}

	.email {
		margin: 0;
		font-weight: var(--font-weight-heading);
		color: var(--color-neutral-900);
		overflow-wrap: anywhere;
	}

	.roles {
		display: flex;
		flex-wrap: wrap;
		gap: var(--space-md);
	}

	.checkbox {
		display: flex;
		align-items: center;
		gap: var(--space-xs);
		font-size: var(--font-size-body);
		color: var(--color-neutral-900);
	}

	.empty {
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
