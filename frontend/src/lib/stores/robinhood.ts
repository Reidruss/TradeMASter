import { writable } from 'svelte/store';
import type { RobinhoodAccountInfo } from '$lib/api';

export const robinhoodAccount = writable<RobinhoodAccountInfo | null>(null);
