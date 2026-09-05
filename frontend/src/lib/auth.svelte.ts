// Client-only auth state. There is no auth API on the backend yet, so login/signup
// just record the email locally; swap the bodies for real API calls once one exists.
import { browser } from '$app/environment';

type User = { email: string };

const STORAGE_KEY = 'auth.user';

function loadUser(): User | null {
	if (!browser) return null;
	const raw = localStorage.getItem(STORAGE_KEY);
	return raw ? (JSON.parse(raw) as User) : null;
}

let user = $state<User | null>(loadUser());

export const auth = {
	get user() {
		return user;
	},
	get isLoggedIn() {
		return user !== null;
	},
	login(email: string) {
		user = { email };
		localStorage.setItem(STORAGE_KEY, JSON.stringify(user));
	},
	signup(email: string) {
		user = { email };
		localStorage.setItem(STORAGE_KEY, JSON.stringify(user));
	},
	logout() {
		user = null;
		localStorage.removeItem(STORAGE_KEY);
	}
};
