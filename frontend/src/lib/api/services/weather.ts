import { api } from '../client';
import type { WeatherForecast } from '../types';

export const weatherService = {
	async getForecast(days: number = 5): Promise<WeatherForecast[]> {
		return api.get<WeatherForecast[]>('/api/weather/forecast', {
			params: { days }
		});
	}
};
