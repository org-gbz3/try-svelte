import { fireEvent, render, screen } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { auth } from '$lib/auth.svelte';
import Page from './+page.svelte';

describe('email confirmation page', () => {
	afterEach(() => { vi.restoreAllMocks(); window.history.replaceState(null, '', '/'); });

	it('リンクを開いただけでは確認せずボタンからトークンを送信する', async () => {
		window.history.replaceState(null, '', '/confirm-email#userId=user-1&token=a%2Bb%2Fc%3D');
		const confirm = vi.spyOn(auth, 'confirmEmail').mockResolvedValue(undefined);
		render(Page);
		expect(confirm).not.toHaveBeenCalled();
		await fireEvent.click(screen.getByRole('button', { name: 'メールアドレスを確認' }));
		expect(confirm).toHaveBeenCalledWith('user-1', 'a+b/c=');
		expect((await screen.findByRole('status')).textContent).toContain('ログインできます');
		expect(window.location.hash).toBe('');
	});

	it('期限切れの応答を表示し再送へのリンクを用意する', async () => {
		window.history.replaceState(null, '', '/confirm-email#userId=user-1&token=expired');
		vi.spyOn(auth, 'confirmEmail').mockRejectedValue(new Error('確認リンクが無効か期限切れです。'));
		render(Page);
		await fireEvent.click(screen.getByRole('button', { name: 'メールアドレスを確認' }));
		expect((await screen.findByRole('alert')).textContent).toContain('期限切れ');
		expect(screen.getByRole('link', { name: 'ログイン・確認メールの再送' }).getAttribute('href')).toBe('/login');
	});
});
