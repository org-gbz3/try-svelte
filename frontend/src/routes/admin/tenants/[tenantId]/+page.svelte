<script lang="ts">
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import { apiFetch, auth, csrfRequest, responseError, PermissionLevel } from '$lib/auth.svelte';

	type TenantRole = { id: string; name: string };
	type TenantMember = { userId: string; email: string; roleIds: string[] };
	type TenantDetail = { id: string; name: string; createdAt: string; roles: TenantRole[]; members: TenantMember[] };

	const tenantId = $derived(page.params.tenantId);

	let tenant = $state<TenantDetail | null>(null);
	let loading = $state(true);
	let forbidden = $state(false);
	let notFound = $state(false);
	let loadError = $state('');

	let renameName = $state('');
	let renaming = $state(false);
	let renameError = $state('');

	let deleting = $state(false);
	let deleteError = $state('');

	let newMemberEmail = $state('');
	let adding = $state(false);
	let addError = $state('');

	// メンバーごとの編集中のロール割り当て。保存までサーバーの状態とは独立して保持する。
	let editedRoles = $state<Record<string, string[]>>({});
	let busyMemberId = $state<string | null>(null);
	let memberError = $state<Record<string, string>>({});
	let savedMemberId = $state<string | null>(null);

	const canWrite = $derived(auth.hasPermission('Admin.Tenants', PermissionLevel.Write));

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
		notFound = false;
		try {
			const response = await apiFetch(`/api/admin/tenants/${tenantId}`);
			if (response.status === 403) {
				forbidden = true;
				return;
			}
			if (response.status === 404) {
				notFound = true;
				return;
			}
			if (!response.ok) throw new Error('テナント情報を取得できませんでした。');
			const detail: TenantDetail = await response.json();
			tenant = detail;
			renameName = detail.name;
			editedRoles = Object.fromEntries(detail.members.map((member) => [member.userId, [...member.roleIds]]));
		} catch (cause) {
			loadError = errorMessage(cause, '読み込みに失敗しました。');
		} finally {
			loading = false;
		}
	}

	onMount(() => { void load(); });

	async function renameTenant(event: SubmitEvent) {
		event.preventDefault();
		if (renaming) return;
		const name = renameName.trim();
		if (!name) { renameError = 'テナント名を入力してください。'; return; }
		renaming = true;
		renameError = '';
		try {
			const response = await csrfRequest(`/api/admin/tenants/${tenantId}`, 'PUT', { name });
			if (!response.ok) throw await responseError(response, 'テナント名を変更できませんでした。');
			await load();
		} catch (cause) {
			renameError = errorMessage(cause, '変更に失敗しました。');
		} finally {
			renaming = false;
		}
	}

	async function deleteTenant() {
		if (!tenant || deleting) return;
		if (!confirm(`テナント「${tenant.name}」を削除しますか? 所属とテナントロールもすべて削除されます。`)) return;
		deleting = true;
		deleteError = '';
		try {
			const response = await csrfRequest(`/api/admin/tenants/${tenantId}`, 'DELETE');
			if (!response.ok) throw await responseError(response, 'テナントを削除できませんでした。');
			await goto('/admin/tenants');
		} catch (cause) {
			deleteError = errorMessage(cause, '削除に失敗しました。');
		} finally {
			deleting = false;
		}
	}

	async function addMember(event: SubmitEvent) {
		event.preventDefault();
		if (adding) return;
		const email = newMemberEmail.trim();
		if (!email) { addError = 'メールアドレスを入力してください。'; return; }
		adding = true;
		addError = '';
		try {
			const response = await csrfRequest(`/api/admin/tenants/${tenantId}/members`, 'POST', { email });
			if (!response.ok) throw await responseError(response, 'メンバーを追加できませんでした。');
			newMemberEmail = '';
			await load();
		} catch (cause) {
			addError = errorMessage(cause, '追加に失敗しました。');
		} finally {
			adding = false;
		}
	}

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
				`/api/admin/tenants/${tenantId}/members/${member.userId}/roles`,
				'PUT',
				{ roleIds: editedRoles[member.userId] ?? [] }
			);
			if (!response.ok) throw await responseError(response, 'ロールを保存できませんでした。');
			await load();
			savedMemberId = member.userId;
		} catch (cause) {
			memberError = { ...memberError, [member.userId]: errorMessage(cause, '保存に失敗しました。') };
		} finally {
			busyMemberId = null;
		}
	}

	async function removeMember(member: TenantMember) {
		if (busyMemberId) return;
		if (!confirm(`${member.email} の所属を解除しますか?`)) return;
		busyMemberId = member.userId;
		memberError = { ...memberError, [member.userId]: '' };
		try {
			const response = await csrfRequest(`/api/admin/tenants/${tenantId}/members/${member.userId}`, 'DELETE');
			if (!response.ok) throw await responseError(response, '所属を解除できませんでした。');
			await load();
		} catch (cause) {
			memberError = { ...memberError, [member.userId]: errorMessage(cause, '解除に失敗しました。') };
		} finally {
			busyMemberId = null;
		}
	}
</script>

{#if auth.isLoggedIn}
	<main>
		<h1>テナントの編集</h1>
		<p class="back"><a href="/admin/tenants">テナント一覧に戻る</a></p>

		{#if loading && !tenant}
			<p role="status">読み込み中です…</p>
		{:else if forbidden}
			<p class="card" role="alert">この画面を利用する権限がありません。</p>
		{:else if notFound}
			<p class="card" role="alert">このテナントは見つかりません。</p>
		{:else if loadError}
			<div class="card">
				<p role="alert">{loadError}</p>
				<button type="button" onclick={() => load()}>再試行</button>
			</div>
		{:else if tenant}
			<section class="card">
				{#if canWrite}
					<form onsubmit={renameTenant} class="inline-form">
						<label>
							テナント名
							<input bind:value={renameName} maxlength="256" required />
						</label>
						<button type="submit" disabled={renaming}>名称を変更</button>
					</form>
					{#if renameError}<p class="error" role="alert">{renameError}</p>{/if}
				{:else}
					<h2>{tenant.name}</h2>
				{/if}
			</section>

			<section class="card">
				<h2>メンバー</h2>
				<ul class="member-list">
					{#each tenant.members as member (member.userId)}
						<li class="member">
							<p class="email">{member.email}</p>
							<div class="roles">
								{#each tenant.roles as role (role.id)}
									<label class="checkbox">
										<input
											type="checkbox"
											aria-label={`${member.email}: ${role.name}`}
											checked={(editedRoles[member.userId] ?? []).includes(role.id)}
											disabled={!canWrite}
											onchange={() => toggleRole(member.userId, role.id)}
										/>
										{role.name}
									</label>
								{/each}
							</div>
							{#if canWrite}
								<div class="actions">
									<button
										type="button"
										aria-label={`${member.email} のロールを保存`}
										disabled={busyMemberId !== null}
										onclick={() => saveMemberRoles(member)}
									>
										ロールを保存
									</button>
									<button
										type="button"
										class="danger"
										aria-label={`${member.email} の所属を解除`}
										disabled={busyMemberId !== null}
										onclick={() => removeMember(member)}
									>
										所属を解除
									</button>
								</div>
							{/if}
							{#if memberError[member.userId]}<p class="error" role="alert">{memberError[member.userId]}</p>{/if}
							{#if savedMemberId === member.userId}<p role="status">保存しました。</p>{/if}
						</li>
					{:else}
						<li class="empty">メンバーがまだいません。</li>
					{/each}
				</ul>

				{#if canWrite}
					<form onsubmit={addMember} class="inline-form">
						<label>
							追加するユーザーのメールアドレス
							<input type="email" bind:value={newMemberEmail} maxlength="254" required />
						</label>
						<button type="submit" disabled={adding}>追加</button>
					</form>
					{#if addError}<p class="error" role="alert">{addError}</p>{/if}
				{/if}
			</section>

			{#if canWrite}
				<section class="card">
					<button type="button" class="danger" onclick={deleteTenant} disabled={deleting}>テナントを削除</button>
					{#if deleteError}<p class="error" role="alert">{deleteError}</p>{/if}
				</section>
			{/if}
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
		display: flex;
		flex-direction: column;
		gap: var(--space-md);
		background: #fff;
		border-radius: var(--radius-card);
		box-shadow: var(--shadow-md);
		padding: var(--space-2xl);
	}

	h2 {
		font-size: var(--font-size-h4);
		margin: 0;
	}

	.inline-form {
		display: flex;
		align-items: flex-end;
		flex-wrap: wrap;
		gap: var(--space-md);
	}

	.inline-form label {
		flex: 1;
		min-width: 200px;
		display: flex;
		flex-direction: column;
		gap: var(--space-xs);
		font-size: var(--font-size-body);
		color: var(--color-neutral-700);
	}

	.inline-form input {
		padding: var(--space-md) var(--space-lg);
		font-family: var(--font-family-base);
		font-size: var(--font-size-body);
		color: var(--color-neutral-900);
		background: var(--color-neutral-50);
		border: 1px solid var(--color-neutral-300);
		border-radius: var(--radius-input);
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

	.roles,
	.actions {
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

	button.danger {
		background: var(--color-danger);
	}

	button.danger:hover {
		background: var(--color-danger);
		opacity: 0.85;
	}

	.error {
		margin: 0;
		font-size: var(--font-size-body);
		color: var(--color-danger);
	}
</style>
