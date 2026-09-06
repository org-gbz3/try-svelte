import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const { gotoMock } = vi.hoisted(() => ({ gotoMock: vi.fn() }));
vi.mock('$app/navigation', () => ({ goto: gotoMock }));

import { auth } from '$lib/auth.svelte';
import Page from './+page.svelte';

describe('signup page', () => {
	beforeEach(() => {
		gotoMock.mockClear();
	});

	afterEach(() => {
		vi.restoreAllMocks();
	});

	it('確認用パスワード欄にもパスワード欄と同じ長さ制限を設定する', () => {
		render(Page);
		const password = screen.getByLabelText('パスワード') as HTMLInputElement;
		const confirm = screen.getByLabelText('パスワード（確認）') as HTMLInputElement;

		expect(confirm.minLength).toBe(password.minLength);
		expect(confirm.maxLength).toBe(password.maxLength);
	});

	it('パスワードが一致しない場合はエラーを表示し signup を呼ばない', async () => {
		const signupSpy = vi.spyOn(auth, 'signup');
		render(Page);

		await fireEvent.input(screen.getByLabelText('メールアドレス'), { target: { value: 'a@example.com' } });
		await fireEvent.input(screen.getByLabelText('パスワード'), { target: { value: 'Password-123!ABC' } });
		await fireEvent.input(screen.getByLabelText('パスワード（確認）'), { target: { value: '一致しない値' } });
		await fireEvent.click(screen.getByRole('button', { name: 'アカウント作成' }));

		expect((await screen.findByRole('alert')).textContent).toContain('パスワードが一致しません');
		expect(signupSpy).not.toHaveBeenCalled();
	});

	it('パスワードが一致する場合は signup を呼び、ログイン画面へ遷移する', async () => {
		const signupSpy = vi.spyOn(auth, 'signup').mockResolvedValue(undefined);
		render(Page);

		await fireEvent.input(screen.getByLabelText('メールアドレス'), { target: { value: 'a@example.com' } });
		await fireEvent.input(screen.getByLabelText('パスワード'), { target: { value: 'Password-123!ABC' } });
		await fireEvent.input(screen.getByLabelText('パスワード（確認）'), { target: { value: 'Password-123!ABC' } });
		await fireEvent.click(screen.getByRole('button', { name: 'アカウント作成' }));

		await waitFor(() => expect(signupSpy).toHaveBeenCalledWith('a@example.com', 'Password-123!ABC'));
		await waitFor(() => expect(gotoMock).toHaveBeenCalledWith('/login?registered=1'));
	});

	it('signup が失敗した場合はサーバーのメッセージを表示する', async () => {
		vi.spyOn(auth, 'signup').mockRejectedValue(new Error('このメールアドレスでは登録できません。'));
		render(Page);

		await fireEvent.input(screen.getByLabelText('メールアドレス'), { target: { value: 'a@example.com' } });
		await fireEvent.input(screen.getByLabelText('パスワード'), { target: { value: 'Password-123!ABC' } });
		await fireEvent.input(screen.getByLabelText('パスワード（確認）'), { target: { value: 'Password-123!ABC' } });
		await fireEvent.click(screen.getByRole('button', { name: 'アカウント作成' }));

		expect((await screen.findByRole('alert')).textContent).toContain('このメールアドレスでは登録できません。');
		expect(gotoMock).not.toHaveBeenCalled();
	});
});
