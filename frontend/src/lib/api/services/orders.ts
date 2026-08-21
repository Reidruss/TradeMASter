import { api } from '../client';
import type { Order, CreateOrderRequest, OrderStatus } from '../types';

export const orderService = {
	getOrders: (status?: OrderStatus) => {
		const query = status !== undefined ? `?status=${status}` : '';
		return api.get<Order[]>(`/api/orders${query}`);
	},

	submitOrder: (request: CreateOrderRequest) =>
		api.post<Order>('/api/orders', request),

	cancelOrder: (orderId: string) =>
		api.delete<{ orderId: string; success: boolean; message: string }>(`/api/orders/${orderId}`)
};
