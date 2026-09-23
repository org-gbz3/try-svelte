import { fireEvent, render, screen } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { auth } from '$lib/auth.svelte';
import Page from './+page.svelte';

describe('forgot password page', () => {
	afterEach(() => {
		vi.restoreAllMocks();
	});

	it('送信に成功するとサーバーの応答メッセージを表示する', async () => {
		vi.spyOn(auth, 'requestPasswordReset').mockResolvedValue(
			'登録されたメールアドレスの場合、パスワード再設定用のメールを送信します。届かない場合は時間をおいて再試行してください。'
		);
		render(Page);

		await fireEvent.input(screen.getByLabelText('メールアドレス'), { target: { value: 'a@example.com' } });
		await fireEvent.click(screen.getByRole('button', { name: '再設定メールを送信' }));

		expect((await screen.findByRole('status')).textContent).toContain('パスワード再設定用のメールを送信します');
	});

	it('送信が失敗した場合はサーバーのメッセージを表示する', async () => {
		vi.spyOn(auth, 'requestPasswordReset').mockRejectedValue(new Error('再設定メールの送信を受け付けられませんでした。'));
		render(Page);

		await fireEvent.input(screen.getByLabelText('メールアドレス'), { target: { value: 'a@example.com' } });
		await fireEvent.click(screen.getByRole('button', { name: '再設定メールを送信' }));

		expect((await screen.findByRole('alert')).textContent).toContain('再設定メールの送信を受け付けられませんでした。');
	});
});
