# TradeMASter 🚀

A modern, production-ready full-stack application combining an **ASP.NET Core Minimal API** backend (.NET 10 / 9 / 8) with a reactive **SvelteKit** frontend powered by **Svelte 5 runes**.

Designed specifically for **effortless, frictionless communication** between frontend and backend in both local development and production.

---

## ✨ Features

- ⚡ **Zero-Friction Dev Proxy:** Vite dev server automatically proxies `/api`, `/scalar`, and `/openapi` requests to the ASP.NET Core backend. No CORS configuration hurdles or hardcoded localhost ports.
- 🔒 **Type-Safe API Layer:** Reusable, strongly-typed `apiClient` (`$lib/api`) with TypeScript models matching backend C# records/DTOs.
- 🎯 **ASP.NET Core Minimal APIs:** Clean, high-performance endpoint architecture with Route Groups, Dependency Injection, and OpenAPI metadata.
- 📖 **Interactive Scalar OpenAPI Docs:** Built-in modern API reference available at `/scalar/v1` or directly embedded in the frontend UI.
- 🧩 **Svelte 5 Runes & Components:** Uses `$state`, `$derived`, and snippets for clean, reactive state management.
- 🧪 **Full CRUD & GET Examples:**
  - Health check & latency monitor (`/api/health`)
  - Weather forecast query stream (`/api/weather/forecast`)
  - Full RESTful Todo management with optimistic updates (`/api/todos`)
  - Interactive API console to test custom endpoints live.
- 🏛️ **Full Multi-Agent Architecture:** Detailed system design specification available in [ARCHITECTURE.md](ARCHITECTURE.md).
- 🛠️ **Single-Command Startup:** Launch both frontend and backend concurrently with hot reloading using `npm run dev`.

---

## 📁 Project Structure

```text
TradeMASter/
├── backend/
│   ├── TradeMASter.Api/
│   │   ├── Endpoints/            # Minimal API endpoint route groups
│   │   │   ├── HealthEndpoints.cs
│   │   │   ├── WeatherEndpoints.cs
│   │   │   └── TodoEndpoints.cs
│   │   ├── Models/               # C# records and DTOs
│   │   ├── Services/             # Business logic & repository services
│   │   ├── Program.cs            # App configuration, DI, CORS & middleware
│   │   └── appsettings.json
│   └── TradeMASter.slnx         # .NET Solution file
│
├── frontend/
│   ├── src/
│   │   ├── lib/
│   │   │   ├── api/              # Typed API communication layer
│   │   │   │   ├── client.ts     # Type-safe fetch wrapper with error handling
│   │   │   │   ├── types.ts      # TypeScript interfaces matching backend models
│   │   │   │   ├── services/     # Modular domain services (todos, weather, health)
│   │   │   │   └── index.ts
│   │   │   └── components/       # Svelte 5 UI components
│   │   ├── routes/               # SvelteKit pages & layouts
│   │   ├── app.css               # Modern CSS design tokens
│   │   └── app.html
│   └── vite.config.ts            # Vite proxy configuration
│
├── .vscode/                      # VS Code launch and task configs
├── package.json                  # Root orchestration scripts
└── README.md
```

---

## 🚀 Quick Start

### 1. Prerequisites
- [.NET SDK](https://dotnet.microsoft.com/download) (10.0 / 9.0 / 8.0)
- [Node.js](https://nodejs.org/) (v18+ or v20+)

### 2. Install Root Dependencies
```bash
npm install
```

### 3. Start Both Backend and Frontend
```bash
npm run dev
```

This single command starts:
- **Backend API:** `http://localhost:5126` (with `dotnet watch` for hot reload)
- **Frontend App:** `http://localhost:5173` (with Vite HMR)
- **Scalar API Docs:** `http://localhost:5173/scalar/v1`

---

## 🔌 How Communication Works

### Development Mode (Vite Proxy)
In `frontend/vite.config.ts`, Vite is configured to proxy all `/api` calls to the .NET backend:

```typescript
// frontend/vite.config.ts
export default defineConfig({
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: process.env.BACKEND_URL || 'http://localhost:5126',
        changeOrigin: true
      }
    }
  }
});
```

Because of this proxy, the SvelteKit frontend simply fetches `/api/...`, avoiding CORS issues and domain mismatch in development.

### Type-Safe API Client
All requests go through the typed API client helper in `frontend/src/lib/api`:

```typescript
import { api } from '$lib/api';
import type { TodoItem, CreateTodoRequest } from '$lib/api';

// GET request
const todos = await api.get<TodoItem[]>('/api/todos');

// POST request
const newTodo = await api.post<TodoItem>('/api/todos', {
  title: 'My new task'
});

// DELETE request
await api.delete(`/api/todos/${id}`);
```

---

## 📝 How to Add a New Endpoint

### Step 1: Create Backend Model & Endpoint
Create a model in `backend/TradeMASter.Api/Models/Product.cs`:
```csharp
namespace TradeMASter.Api.Models;

public record Product(Guid Id, string Name, decimal Price);
public record CreateProductRequest(string Name, decimal Price);
```

Add an endpoint group in `backend/TradeMASter.Api/Endpoints/ProductEndpoints.cs`:
```csharp
namespace TradeMASter.Api.Endpoints;

public static class ProductEndpoints
{
    public static RouteGroupBuilder MapProductEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/products").WithTags("Products");

        group.MapGet("/", () => Results.Ok(new[] {
            new Product(Guid.NewGuid(), "Mechanical Keyboard", 129.99m)
        }));

        return group;
    }
}
```

Register it in `backend/TradeMASter.Api/Program.cs`:
```csharp
app.MapProductEndpoints();
```

---

### Step 2: Add TypeScript Types & Service Method
In `frontend/src/lib/api/types.ts`:
```typescript
export interface Product {
  id: string;
  name: string;
  price: number;
}
```

In `frontend/src/lib/api/services/products.ts`:
```typescript
import { api } from '../client';
import type { Product } from '../types';

export const productService = {
  getProducts: () => api.get<Product[]>('/api/products')
};
```

Export it in `frontend/src/lib/api/index.ts`.

---

### Step 3: Consume in Svelte Component
In any Svelte 5 component (`.svelte`):
```svelte
<script lang="ts">
  import { onMount } from 'svelte';
  import { productService, type Product } from '$lib/api';

  let products = $state<Product[]>([]);

  onMount(async () => {
    products = await productService.getProducts();
  });
</script>

<ul>
  {#each products as product}
    <li>{product.name} — ${product.price}</li>
  {/each}
</ul>
```

---

## 📦 Available Scripts

| Command | Description |
| :--- | :--- |
| `npm run dev` | Runs both backend and frontend concurrently in watch mode |
| `npm run dev:backend` | Runs the ASP.NET Core API with `dotnet watch` |
| `npm run dev:frontend` | Runs the SvelteKit frontend dev server |
| `npm run build` | Builds both frontend and backend for production |
| `npm run build:frontend`| Builds the SvelteKit production bundle |
| `npm run build:backend` | Builds the .NET solution in Release configuration |
| `npm run check` | Runs SvelteKit type checking (`svelte-check`) |

---

## 🚢 Production Deployment

### Option A: Standalone Containers / Services (Recommended)
- Host ASP.NET Core API as a backend container / App Service.
- Host SvelteKit (Node adapter or Cloudflare / Vercel adapter) pointing `BACKEND_URL` to your production API URL.

### Option B: Single Host (ASP.NET Core Serves Static SvelteKit)
1. In `frontend/svelte.config.js`, configure `@sveltejs/adapter-static`.
2. Build frontend: `npm run build:frontend`.
3. Copy static build artifacts to `backend/TradeMASter.Api/wwwroot`.
4. Run `dotnet publish`. ASP.NET Core will serve the static SPA and API from a single port with fallback routing.
