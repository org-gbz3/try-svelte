<script lang="ts">
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { apiFetch, auth } from '$lib/auth.svelte';

	type UserListItem = { id: string; email: string; createdAt: string | null };
	type UserListResponse = { items: UserListItem[]; totalCount: number; page: number; pageSize: number };
	type SortField = 'email' | 'createdAt';
	type SortDirection = 'ascending' | 'descending';

	const pageSize = 20;

	let users = $state<UserListItem[]>([]);
	let totalCount = $state(0);
	let page = $state(1);
	let sort = $state<SortField>('createdAt');
	let direction = $state<SortDirection>('descending');
	let emailInput = $state('');
	let emailFilter = $state('');
	let prefixMatchInput = $state(false);
	let prefixMatchFilter = $state(false);
	let caseSensitiveInput = $state(false);
	let caseSensitiveFilter = $state(false);
	let loading = $state(true);
	let forbidden = $state(false);
	let loadError = $state('');

	$effect(() => {
		if (auth.status === 'anonymous') goto('/login');
	});

	function buildQuery(): string {
		const params = new URLSearchParams();
		if (emailFilter) params.set('email', emailFilter);
		if (prefixMatchFilter) params.set('prefixMatch', 'true');
		if (caseSensitiveFilter) params.set('caseSensitive', 'true');
		params.set('sort', sort);
		params.set('direction', direction);
		params.set('page', String(page));
		params.set('pageSize', String(pageSize));
		return `/api/admin/users?${params.toString()}`;
	}

	async function load() {
		loading = true;
		loadError = '';
		forbidden = false;
		try {
			const response = await apiFetch(buildQuery());
			if (response.status === 403) {
				forbidden = true;
				return;
			}
			if (!response.ok) throw new Error('ユーザー一覧を取得できませんでした。');
			const body: UserListResponse = await response.json();
			users = body.items;
			totalCount = body.totalCount;
		} catch (cause) {
			loadError = cause instanceof TypeError
				? '通信に失敗しました。接続を確認してください。'
				: cause instanceof Error ? cause.message : '読み込みに失敗しました。';
		} finally {
			loading = false;
		}
	}

	onMount(() => { void load(); });

	function submitSearch(event: SubmitEvent) {
		event.preventDefault();
		emailFilter = emailInput.trim();
		prefixMatchFilter = prefixMatchInput;
		caseSensitiveFilter = caseSensitiveInput;
		page = 1;
		void load();
	}

	function toggleSort(field: SortField) {
		if (sort === field) {
			direction = direction === 'ascending' ? 'descending' : 'ascending';
		} else {
			sort = field;
			direction = field === 'createdAt' ? 'descending' : 'ascending';
		}
		page = 1;
		void load();
	}

	function sortIndicator(field: SortField): string {
		if (sort !== field) return '';
		return direction === 'ascending' ? ' ▲' : ' ▼';
	}

	function formatDate(value: string | null): string {
		if (!value) return '-';
		return new Date(value).toLocaleString('ja-JP');
	}

	function goToPreviousPage() {
		page -= 1;
		void load();
	}

	function goToNextPage() {
		page += 1;
		void load();
	}
</script>

{#if auth.isLoggedIn}
	<main>
		<h1>ユーザー管理</h1>
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
			<div class="card">
				<form class="search-form" onsubmit={submitSearch}>
					<label>
						メールアドレスで検索
						<input bind:value={emailInput} placeholder="例: example.com" />
					</label>
					<label class="checkbox-label">
						<input type="checkbox" bind:checked={prefixMatchInput} />
						前方一致検索
					</label>
					<label class="checkbox-label">
						<input type="checkbox" bind:checked={caseSensitiveInput} />
						大文字小文字を区別
					</label>
					<button type="submit">検索</button>
				</form>

				<table>
					<thead>
						<tr>
							<th scope="col">
								<button type="button" class="sort-button" onclick={() => toggleSort('email')}>
									メールアドレス{sortIndicator('email')}
								</button>
							</th>
							<th scope="col">
								<button type="button" class="sort-button" onclick={() => toggleSort('createdAt')}>
									登録日時{sortIndicator('createdAt')}
								</button>
							</th>
							<th scope="col">操作</th>
						</tr>
					</thead>
					<tbody>
						{#each users as user (user.id)}
							<tr>
								<td>{user.email}</td>
								<td>{formatDate(user.createdAt)}</td>
								<td><a href={`/admin/users/${user.id}/roles?email=${encodeURIComponent(user.email)}`}>ロールを編集</a></td>
							</tr>
						{:else}
							<tr><td colspan="3" class="empty">該当するユーザーがいません。</td></tr>
						{/each}
					</tbody>
				</table>

				<div class="pagination">
					<button type="button" disabled={page <= 1} onclick={goToPreviousPage}>前へ</button>
					<span>
						{totalCount === 0 ? 0 : (page - 1) * pageSize + 1}–{Math.min(page * pageSize, totalCount)} / {totalCount}件
					</span>
					<button type="button" disabled={page * pageSize >= totalCount} onclick={goToNextPage}>次へ</button>
				</div>
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
		display: flex;
		flex-direction: column;
		gap: var(--space-lg);
	}

	.search-form {
		display: flex;
		flex-direction: row;
		align-items: flex-end;
		flex-wrap: wrap;
		gap: var(--space-md);
	}

	.search-form label {
		flex: 1;
		min-width: 200px;
		display: flex;
		flex-direction: column;
		gap: var(--space-xs);
		font-size: var(--font-size-body);
		color: var(--color-neutral-700);
	}

	.search-form input {
		padding: var(--space-md) var(--space-lg);
		font-family: var(--font-family-base);
		font-size: var(--font-size-body);
		color: var(--color-neutral-900);
		background: var(--color-neutral-50);
		border: 1px solid var(--color-neutral-300);
		border-radius: var(--radius-input);
	}

	.checkbox-label {
		flex-direction: row;
		align-items: center;
		gap: var(--space-xs);
		white-space: nowrap;
	}

	.checkbox-label input {
		width: auto;
		padding: 0;
	}

	table {
		width: 100%;
		border-collapse: collapse;
	}

	th,
	td {
		text-align: left;
		padding: var(--space-sm) var(--space-md);
		border-bottom: 1px solid var(--color-neutral-200);
		font-size: var(--font-size-body);
	}

	.sort-button {
		background: none;
		border: none;
		padding: 0;
		font-family: var(--font-family-base);
		font-size: var(--font-size-body);
		font-weight: var(--font-weight-heading);
		color: var(--color-neutral-900);
		cursor: pointer;
		box-shadow: none;
	}

	.empty {
		color: var(--color-neutral-500);
		text-align: center;
	}

	.pagination {
		display: flex;
		align-items: center;
		gap: var(--space-md);
	}

	.pagination span {
		font-size: var(--font-size-caption);
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

	button:disabled {
		opacity: 0.4;
		cursor: not-allowed;
	}

	a {
		color: var(--color-primary);
	}
</style>
