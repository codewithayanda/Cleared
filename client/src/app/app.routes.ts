import { Routes } from '@angular/router';

// Two lazy groups, kept deliberately separate:
//
//   /pay/:token   unauthenticated, opened by the Owner's customer, often on a
//                 mid-range phone over mobile data. Must stay under the payment
//                 page bundle budget (NFR P-11), so nothing from the authenticated
//                 app may leak into this chunk.
//
//   everything else  authenticated, behind authGuard.
export const routes: Routes = [];
