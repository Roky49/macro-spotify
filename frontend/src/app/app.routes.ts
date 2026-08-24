import { Routes } from '@angular/router';
import { SpotifyComponent } from './components/spotify/spotify.component';
export const routes: Routes = [{ path: '', component: SpotifyComponent }, { path: '**', redirectTo: '' }];
