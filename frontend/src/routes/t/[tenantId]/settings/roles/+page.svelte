<script lang="ts">
	import { page } from '$app/state';
	import RoleManager from '$lib/components/RoleManager.svelte';

	const tenantId = $derived(page.params.tenantId);
</script>

{#key tenantId}
	<RoleManager
		title="テナントロール管理"
		apiBase={`/api/tenants/${tenantId}/roles`}
		backHref={`/t/${tenantId}`}
		backLabel="テナントのトップに戻る"
	/>
{/key}
