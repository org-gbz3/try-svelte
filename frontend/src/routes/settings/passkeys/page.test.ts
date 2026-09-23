import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';

const { gotoMock } = vi.hoisted(() => ({ gotoMock: vi.fn() }));
vi.mock('$app/navigation', () => ({ goto: gotoMock }));

import { auth } from '$lib/auth.svelte';
import Page from './+page.svelte';

describe('passkey settings page', () => {
	afterEach(() => {
		vi.restoreAllMocks();
		vi.unstubAllGlobals();
		gotoMock.mockClear();
	});

	it('非対応ブラウザーでは案内を表示し、追加操作を出さない', async () => {
		vi.spyOn(auth, 'listPasskeys').mockResolvedValue([]);
		render(Page);

		expect((await screen.findByRole('alert')).textContent).toContain('対応していません');
		expect(screen.queryByRole('button', { name: 'このデバイスにパスキーを追加' })).toBeNull();
	});

	it('登録済みのパスキーを一覧表示する', async () => {
		vi.stubGlobal('PublicKeyCredential', {});
		vi.spyOn(auth, 'listPasskeys').mockResolvedValue([
			{ id: 'a', name: '自宅のPC', createdAt: null, isBackedUp: true }
		]);
		render(Page);

		expect(await screen.findByText('自宅のPC')).not.toBeNull();
	});

	it('追加に成功すると一覧を再取得する', async () => {
		vi.stubGlobal('PublicKeyCredential', {});
		const listSpy = vi.spyOn(auth, 'listPasskeys').mockResolvedValue([]);
		const registerSpy = vi.spyOn(auth, 'registerPasskey').mockResolvedValue(undefined);
		render(Page);
		await waitFor(() => expect(listSpy).toHaveBeenCalledTimes(1));

		await fireEvent.input(screen.getByLabelText('名前(任意)'), { target: { value: '自分のPC' } });
		await fireEvent.click(screen.getByRole('button', { name: 'このデバイスにパスキーを追加' }));

		await waitFor(() => expect(registerSpy).toHaveBeenCalledWith('自分のPC'));
		await waitFor(() => expect(listSpy).toHaveBeenCalledTimes(2));
	});

	it('追加が失敗した場合はエラーメッセージを表示する', async () => {
		vi.stubGlobal('PublicKeyCredential', {});
		vi.spyOn(auth, 'listPasskeys').mockResolvedValue([]);
		vi.spyOn(auth, 'registerPasskey').mockRejectedValue(new Error('パスキーの作成がキャンセルされたか失敗しました。'));
		render(Page);

		await fireEvent.click(await screen.findByRole('button', { name: 'このデバイスにパスキーを追加' }));

		expect((await screen.findByRole('alert')).textContent).toContain('パスキーの作成がキャンセルされたか失敗しました。');
	});

	it('削除に成功すると一覧から取り除かれる', async () => {
		vi.stubGlobal('PublicKeyCredential', {});
		const listSpy = vi.spyOn(auth, 'listPasskeys')
			.mockResolvedValueOnce([{ id: 'a', name: '自宅のPC', createdAt: null, isBackedUp: false }])
			.mockResolvedValueOnce([]);
		const removeSpy = vi.spyOn(auth, 'removePasskey').mockResolvedValue(undefined);
		render(Page);
		await screen.findByText('自宅のPC');

		await fireEvent.click(screen.getByRole('button', { name: '削除' }));

		await waitFor(() => expect(removeSpy).toHaveBeenCalledWith('a'));
		await waitFor(() => expect(listSpy).toHaveBeenCalledTimes(2));
		expect(screen.queryByText('自宅のPC')).toBeNull();
	});
});
