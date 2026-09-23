import { fireEvent, render, screen } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { auth } from '$lib/auth.svelte';
import Page from './+page.svelte';

describe('reset password page', () => {
	afterEach(() => {
		vi.restoreAllMocks();
		window.history.replaceState(null, '', '/');
	});

	it('リンクが不正な場合はエラーを表示する', async () => {
		window.history.replaceState(null, '', '/reset-password');
		render(Page);

		expect((await screen.findByRole('alert')).textContent).toContain('再設定リンクが不正です');
	});

	it('パスワードが一致しない場合はエラーを表示しresetPasswordを呼ばない', async () => {
		window.history.replaceState(null, '', '/reset-password#userId=user-1&token=a%2Bb%2Fc%3D');
		const resetSpy = vi.spyOn(auth, 'resetPassword');
		render(Page);

		await fireEvent.input(screen.getByLabelText('新しいパスワード'), { target: { value: 'Password-123!ABC' } });
		await fireEvent.input(screen.getByLabelText('新しいパスワード（確認）'), { target: { value: '一致しない値12345' } });
		await fireEvent.click(screen.getByRole('button', { name: 'パスワードを再設定' }));

		expect((await screen.findByRole('alert')).textContent).toContain('パスワードが一致しません');
		expect(resetSpy).not.toHaveBeenCalled();
	});

	it('フラグメントから取得したuserId・tokenで再設定し、成功後はハッシュを消去する', async () => {
		window.history.replaceState(null, '', '/reset-password#userId=user-1&token=a%2Bb%2Fc%3D');
		const resetSpy = vi.spyOn(auth, 'resetPassword').mockResolvedValue(undefined);
		render(Page);

		await fireEvent.input(screen.getByLabelText('新しいパスワード'), { target: { value: 'Password-123!ABC' } });
		await fireEvent.input(screen.getByLabelText('新しいパスワード（確認）'), { target: { value: 'Password-123!ABC' } });
		await fireEvent.click(screen.getByRole('button', { name: 'パスワードを再設定' }));

		expect(resetSpy).toHaveBeenCalledWith('user-1', 'a+b/c=', 'Password-123!ABC');
		expect((await screen.findByRole('status')).textContent).toContain('パスワードを再設定しました');
		expect(window.location.hash).toBe('');
	});

	it('再設定に失敗した場合はサーバーのメッセージを表示する', async () => {
		window.history.replaceState(null, '', '/reset-password#userId=user-1&token=expired');
		vi.spyOn(auth, 'resetPassword').mockRejectedValue(new Error('再設定リンクが無効か期限切れです。もう一度お試しください。'));
		render(Page);

		await fireEvent.input(screen.getByLabelText('新しいパスワード'), { target: { value: 'Password-123!ABC' } });
		await fireEvent.input(screen.getByLabelText('新しいパスワード（確認）'), { target: { value: 'Password-123!ABC' } });
		await fireEvent.click(screen.getByRole('button', { name: 'パスワードを再設定' }));

		expect((await screen.findByRole('alert')).textContent).toContain('再設定リンクが無効か期限切れです。');
	});
});
