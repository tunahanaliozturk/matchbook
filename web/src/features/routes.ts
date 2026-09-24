import type { RouteRecordRaw } from "vue-router";

import { routes as budgets } from "./budgets/routes";
import { routes as costCentres } from "./cost-centres/routes";
import { routes as inbox } from "./inbox/routes";
import { routes as purchaseOrders } from "./purchase-orders/routes";
import { routes as suppliers } from "./suppliers/routes";

// Every feature's pages, mounted inside the window. A feature owns its routes, and each route names the roles its
// service's read policy admits (docs/design.md), so navigation and the server agree about who may look.
export const routes: RouteRecordRaw[] = [
    ...inbox,
    ...suppliers,
    ...costCentres,
    ...budgets,
    ...purchaseOrders,
];
