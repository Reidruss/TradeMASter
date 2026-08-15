import { api } from '../client';
import type { TodoItem, CreateTodoRequest, UpdateTodoRequest } from '../types';

export const todoService = {
	async getAll(): Promise<TodoItem[]> {
		return api.get<TodoItem[]>('/api/todos');
	},

	async getById(id: string): Promise<TodoItem> {
		return api.get<TodoItem>(`/api/todos/${id}`);
	},

	async create(request: CreateTodoRequest): Promise<TodoItem> {
		return api.post<TodoItem>('/api/todos', request);
	},

	async update(id: string, request: UpdateTodoRequest): Promise<TodoItem> {
		return api.put<TodoItem>(`/api/todos/${id}`, request);
	},

	async toggleComplete(id: string): Promise<TodoItem> {
		return api.patch<TodoItem>(`/api/todos/${id}/toggle`);
	},

	async delete(id: string): Promise<void> {
		return api.delete<void>(`/api/todos/${id}`);
	}
};
