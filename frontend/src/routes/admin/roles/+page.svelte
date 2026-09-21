<script lang="ts">
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { apiFetch, auth, csrfRequest, responseError, PermissionLevel } from '$lib/auth.svelte';

	type PermissionActionResponse = { actionKey: string; displayName: string };
	type RolePermissionEntry = { actionKey: string; displayName: string; level: PermissionLevel };
	type RoleResponse = { id: string; name: string; permissions: RolePermissionEntry[] };

	const levelColumns: { level: PermissionLevel; label: string }[] = [
		{ level: PermissionLevel.None, label: 'なし' },
		{ level: PermissionLevel.Read, label: '参照' },
		{ level: PermissionLevel.Write, label: '編集' }
	];

	let roles = $state<RoleResponse[]>([]);
	let permissionActions = $state<PermissionActionResponse[]>([]);
	let selectedRoleId = $state<string | null>(null);
	let loading = $state(true);
	let forbidden = $state(false);
	let loadError = $state('');

	let newRoleName = $state('');
	let creating = $state(false);
	let createError = $state('');

	let renameName = $state('');
	let renaming = $state(false);
	let renameError = $state('');

	let deleting = $state(false);
	let deleteError = $state('');

	let editedPermissions = $state<Record<string, PermissionLevel>>({});
	let savingPermissions = $state(false);
	let permissionsError = $state('');

	const selectedRole = $derived(roles.find((role) => role.id === selectedRoleId) ?? null);

	$effect(() => {
		if (auth.status === 'anonymous') goto('/login');
	});

	async function load() {
		loading = true;
		loadError = '';
		forbidden = false;
		try {
			const [rolesResponse, actionsResponse] = await Promise.all([
				apiFetch('/api/admin/roles'),
				apiFetch('/api/admin/roles/permission-actions')
			]);
			if (rolesResponse.status === 403 || actionsResponse.status === 403) {
				forbidden = true;
				return;
			}
			if (!rolesResponse.ok || !actionsResponse.ok) throw new Error('ロール情報を取得できませんでした。');
			roles = await rolesResponse.json();
			permissionActions = await actionsResponse.json();
		} catch (cause) {
			loadError = cause instanceof TypeError
				? '通信に失敗しました。接続を確認してください。'
				: cause instanceof Error ? cause.message : '読み込みに失敗しました。';
		} finally {
			loading = false;
		}
	}

	onMount(() => { void load(); });

	function selectRole(role: RoleResponse) {
		selectedRoleId = role.id;
		renameName = role.name;
		renameError = '';
		deleteError = '';
		permissionsError = '';
		editedPermissions = Object.fromEntries(
			permissionActions.map((action) => [
				action.actionKey,
				role.permissions.find((entry) => entry.actionKey === action.actionKey)?.level ?? PermissionLevel.None
			])
		);
	}

	// 更新後はサーバー側の状態を正として再取得し、選択中ロールがあれば選択状態と編集内容を作り直す。
	async function afterMutation() {
		const previousId = selectedRoleId;
		await load();
		if (previousId) {
			const role = roles.find((candidate) => candidate.id === previousId);
			if (role) selectRole(role);
			else selectedRoleId = null;
		}
	}

	async function createRole(event: SubmitEvent) {
		event.preventDefault();
		if (creating) return;
		const name = newRoleName.trim();
		if (!name) { createError = 'ロール名を入力してください。'; return; }
		creating = true;
		createError = '';
		try {
			const response = await csrfRequest('/api/admin/roles', 'POST', { name });
			if (!response.ok) throw await responseError(response, 'ロールを作成できませんでした。');
			newRoleName = '';
			await load();
		} catch (cause) {
			createError = cause instanceof TypeError
				? '通信に失敗しました。接続を確認してください。'
				: cause instanceof Error ? cause.message : '作成に失敗しました。';
		} finally {
			creating = false;
		}
	}

	async function renameRole(event: SubmitEvent) {
		event.preventDefault();
		if (!selectedRole || renaming) return;
		const name = renameName.trim();
		if (!name) { renameError = 'ロール名を入力してください。'; return; }
		renaming = true;
		renameError = '';
		try {
			const response = await csrfRequest(`/api/admin/roles/${selectedRole.id}`, 'PUT', { name });
			if (!response.ok) throw await responseError(response, 'ロール名を変更できませんでした。');
			await afterMutation();
		} catch (cause) {
			renameError = cause instanceof TypeError
				? '通信に失敗しました。接続を確認してください。'
				: cause instanceof Error ? cause.message : '変更に失敗しました。';
		} finally {
			renaming = false;
		}
	}

	async function deleteRole() {
		if (!selectedRole || deleting) return;
		if (!confirm(`ロール「${selectedRole.name}」を削除しますか?`)) return;
		deleting = true;
		deleteError = '';
		try {
			const response = await csrfRequest(`/api/admin/roles/${selectedRole.id}`, 'DELETE');
			if (!response.ok) throw await responseError(response, 'ロールを削除できませんでした。');
			selectedRoleId = null;
			await load();
		} catch (cause) {
			deleteError = cause instanceof TypeError
				? '通信に失敗しました。接続を確認してください。'
				: cause instanceof Error ? cause.message : '削除に失敗しました。';
		} finally {
			deleting = false;
		}
	}

	async function savePermissions(event: SubmitEvent) {
		event.preventDefault();
		if (!selectedRole || savingPermissions) return;
		savingPermissions = true;
		permissionsError = '';
		try {
			const permissions = permissionActions.map((action) => ({
				actionKey: action.actionKey,
				level: editedPermissions[action.actionKey] ?? PermissionLevel.None
			}));
			const response = await csrfRequest(`/api/admin/roles/${selectedRole.id}/permissions`, 'PUT', { permissions });
			if (!response.ok) throw await responseError(response, '権限を保存できませんでした。');
			await afterMutation();
		} catch (cause) {
			permissionsError = cause instanceof TypeError
				? '通信に失敗しました。接続を確認してください。'
				: cause instanceof Error ? cause.message : '保存に失敗しました。';
		} finally {
			savingPermissions = false;
		}
	}
</script>

{#if auth.isLoggedIn}
	<main>
		<h1>ロール管理</h1>
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
			<div class="layout">
				<section class="card role-list">
					<h2>ロール一覧</h2>
					<ul>
						{#each roles as role (role.id)}
							<li>
								<button
									type="button"
									class="role-button"
									class:selected={role.id === selectedRoleId}
									onclick={() => selectRole(role)}
								>
									{role.name}
								</button>
							</li>
						{:else}
							<li class="empty">ロールがまだありません。</li>
						{/each}
					</ul>
					<form onsubmit={createRole}>
						<label>
							新規ロール名
							<input bind:value={newRoleName} maxlength="256" required />
						</label>
						{#if createError}<p class="error" role="alert">{createError}</p>{/if}
						<button type="submit" disabled={creating}>作成</button>
					</form>
				</section>

				{#if selectedRole}
					<section class="card role-detail">
						<form onsubmit={renameRole} class="rename-form">
							<label>
								ロール名
								<input bind:value={renameName} maxlength="256" required />
							</label>
							{#if renameError}<p class="error" role="alert">{renameError}</p>{/if}
							<button type="submit" disabled={renaming}>名称を変更</button>
						</form>

						<div class="delete-row">
							<button type="button" class="danger" onclick={deleteRole} disabled={deleting}>ロールを削除</button>
							{#if deleteError}<p class="error" role="alert">{deleteError}</p>{/if}
						</div>

						<form onsubmit={savePermissions} class="permissions-form">
							<h3>権限</h3>
							<table>
								<thead>
									<tr>
										<th scope="col">操作</th>
										{#each levelColumns as column (column.level)}
											<th scope="col">{column.label}</th>
										{/each}
									</tr>
								</thead>
								<tbody>
									{#each permissionActions as action (action.actionKey)}
										<tr>
											<th scope="row">{action.displayName}</th>
											{#each levelColumns as column (column.level)}
												<td>
													<input
														type="radio"
														name={`permission-${action.actionKey}`}
														aria-label={`${action.displayName}: ${column.label}`}
														checked={(editedPermissions[action.actionKey] ?? PermissionLevel.None) === column.level}
														onchange={() => {
															editedPermissions = { ...editedPermissions, [action.actionKey]: column.level };
														}}
													/>
												</td>
											{/each}
										</tr>
									{/each}
								</tbody>
							</table>
							{#if permissionsError}<p class="error" role="alert">{permissionsError}</p>{/if}
							<button type="submit" disabled={savingPermissions}>権限を保存</button>
						</form>
					</section>
				{/if}
			</div>
		{/if}
	</main>
{/if}

<style>
	main {
		max-width: 960px;
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

	.layout {
		display: grid;
		grid-template-columns: minmax(220px, 280px) 1fr;
		gap: var(--space-xl);
		align-items: start;
	}

	@media (max-width: 720px) {
		.layout {
			grid-template-columns: 1fr;
		}
	}

	h2 {
		font-size: var(--font-size-h4);
		margin: 0 0 var(--space-lg);
	}

	h3 {
		font-size: var(--font-size-h4);
		margin: var(--space-xl) 0 var(--space-md);
	}

	.role-list ul {
		list-style: none;
		margin: 0 0 var(--space-lg);
		padding: 0;
		display: flex;
		flex-direction: column;
		gap: var(--space-xs);
	}

	.role-list .empty {
		color: var(--color-neutral-500);
		font-size: var(--font-size-caption);
	}

	.role-button {
		width: 100%;
		text-align: left;
		padding: var(--space-md) var(--space-lg);
		font-family: var(--font-family-base);
		font-size: var(--font-size-body);
		color: var(--color-neutral-900);
		background: var(--color-neutral-50);
		border: 1px solid var(--color-neutral-200);
		border-radius: var(--radius-input);
		cursor: pointer;
	}

	.role-button.selected {
		border-color: var(--color-primary);
		background: var(--color-primary-50);
		color: var(--color-primary-700);
		font-weight: var(--font-weight-heading);
	}

	form {
		display: flex;
		flex-direction: column;
		gap: var(--space-md);
	}

	.rename-form {
		flex-direction: row;
		align-items: flex-end;
		flex-wrap: wrap;
	}

	.rename-form label {
		flex: 1;
		min-width: 200px;
	}

	label {
		display: flex;
		flex-direction: column;
		gap: var(--space-xs);
		font-size: var(--font-size-body);
		color: var(--color-neutral-700);
	}

	input:not([type]) {
		padding: var(--space-md) var(--space-lg);
		font-family: var(--font-family-base);
		font-size: var(--font-size-body);
		color: var(--color-neutral-900);
		background: var(--color-neutral-50);
		border: 1px solid var(--color-neutral-300);
		border-radius: var(--radius-input);
	}

	.delete-row {
		margin-top: var(--space-xl);
		padding-top: var(--space-xl);
		border-top: 1px solid var(--color-neutral-200);
		display: flex;
		flex-direction: column;
		gap: var(--space-sm);
		align-items: flex-start;
	}

	table {
		width: 100%;
		border-collapse: collapse;
		margin-bottom: var(--space-md);
	}

	th,
	td {
		text-align: left;
		padding: var(--space-sm) var(--space-md);
		border-bottom: 1px solid var(--color-neutral-200);
		font-size: var(--font-size-body);
	}

	th[scope='col']:not(:first-child),
	td:not(:first-child) {
		text-align: center;
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
