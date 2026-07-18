# FlatWorld Networking Foundation

Installed backend: **Mirror 96.11.0**, using KCP transport by default.

## Boundary rule

Gameplay code depends on `FlatWorld.Networking.Core`, never directly on
Mirror. Only code inside the `Mirror` folder may reference Mirror types.

Key entry points:

- `GameNetwork.Session`: host/client/server lifecycle.
- `GameNetwork.HasStateAuthority`: server-authoritative simulation gate that
  remains `true` in the existing offline game.
- `INetworkEntityContext`: per-object state and input authority.

## Bootstrap when multiplayer implementation starts

1. Create a persistent `NetworkManager` object in a bootstrap scene.
2. Add `FlatWorldNetworkManager`; `KcpTransport` is added automatically.
3. Keep `Auto Create Player` disabled until the player prefab is converted.
4. Add `NetworkIdentity` and `MirrorNetworkEntityContext` to the player prefab.
5. Gate local input with `HasInputAuthority` and world mutation with
   `GameNetwork.HasStateAuthority`.

## Recommended migration order

1. Player spawn, ownership, movement and camera.
2. Interaction commands and server-side validation.
3. Item spawning, inventory and equipment snapshots.
4. Combat, AI and building authority.
5. Chunk/world events, time and weather synchronization.
6. Host/server-only saving and reconnect handling.

Do not replace `Instantiate` with `NetworkServer.Spawn` throughout gameplay.
Add a spawn service behind the Core boundary when item/world synchronization
is implemented, then migrate call sites feature by feature.

## Isolated connection test

Use `FlatWorld > Networking Test > Create or Update Test Scene`, then open
`Assets/3_Scenes/NetworkTest.unity` and press Play. The HUD can start a Host,
Client or dedicated Server. The generated player prefab tests ownership and
client-to-server transform synchronization.

For an automated two-process smoke test, build with `FlatWorld > Networking
Test > Build Windows Test Player`, then run one process with
`-networkRole host` and another with `-networkRole client -networkAddress
127.0.0.1`. Add `-networkAutoMove -networkExitAfter 15` for unattended tests.
