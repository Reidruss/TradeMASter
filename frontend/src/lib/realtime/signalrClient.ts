import * as signalR from '@microsoft/signalr';

class RealtimeConnectionManager {
	private marketHub: signalR.HubConnection | null = null;
	private debateHub: signalR.HubConnection | null = null;

	public getMarketHub(): signalR.HubConnection {
		if (!this.marketHub) {
			this.marketHub = new signalR.HubConnectionBuilder()
				.withUrl('/hubs/market')
				.withAutomaticReconnect([0, 2000, 5000, 10000])
				.configureLogging(signalR.LogLevel.Warning)
				.build();
		}
		return this.marketHub;
	}

	public getDebateHub(): signalR.HubConnection {
		if (!this.debateHub) {
			this.debateHub = new signalR.HubConnectionBuilder()
				.withUrl('/hubs/debate')
				.withAutomaticReconnect([0, 2000, 5000, 10000])
				.configureLogging(signalR.LogLevel.Warning)
				.build();
		}
		return this.debateHub;
	}

	public async startMarketHub(): Promise<signalR.HubConnection> {
		const hub = this.getMarketHub();
		if (hub.state === signalR.HubConnectionState.Disconnected) {
			try {
				await hub.start();
			} catch (err) {
				console.warn('Market Hub connection failed', err);
			}
		}
		return hub;
	}

	public async startDebateHub(): Promise<signalR.HubConnection> {
		const hub = this.getDebateHub();
		if (hub.state === signalR.HubConnectionState.Disconnected) {
			try {
				await hub.start();
			} catch (err) {
				console.warn('Debate Hub connection failed', err);
			}
		}
		return hub;
	}
}

export const realtime = new RealtimeConnectionManager();
